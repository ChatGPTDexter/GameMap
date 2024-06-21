import pandas as pd
import fastcluster
from scipy.cluster import hierarchy as sch
import numpy as np
import uuid
import os
import openai
import matplotlib.pyplot as plt
from umap import UMAP
from scipy.sparse.csgraph import minimum_spanning_tree
import scipy
from sklearn.cluster import KMeans
from sklearn.metrics.pairwise import cosine_distances
from adjustText import adjust_text
from dotenv import load_dotenv
from sklearn.preprocessing import MinMaxScaler

# Load OpenAI API key from environment variable for security
load_dotenv()
api_key = os.getenv("apikey")

openai.api_key = api_key

def get_embeddings_batch(inputs):
    embeddings = []
    for input_text in inputs:
        response = openai.Embedding.create(
            input=input_text,
            model="text-embedding-ada-002"
        )
        embedding = np.array(response['data'][0]['embedding'])
        embeddings.append(embedding)
    return np.array(embeddings)

def cosine_similarity(a, b):
    dot_product = np.dot(a, b)
    norm_a = np.linalg.norm(a)
    norm_b = np.linalg.norm(b)
    return dot_product / (norm_a * norm_b)

class ClusterCreator:
    def __init__(self, max_cluster_depth, min_nodes_per_cluster, min_clusters=7):
        self.max_cluster_depth = max_cluster_depth
        self.min_nodes_per_cluster = min_nodes_per_cluster
        self.min_clusters = min_clusters
        self.cluster_list = []
        self.workbench = None
        self.skill_cluster_mapping = {}
        self.cluster_nodes = {}
        self.connections = None
        self.umap_coords = None
        self.cluster_names = {}
        self.mst_data = None

    def make_clusters(self):
        # Normalize view counts before UMAP transformations
        self.normalize_view_count()
        
        embeddings = np.stack(self.workbench['embedding'])
        kmeans = KMeans(n_clusters=max(self.min_clusters, len(self.workbench) // self.min_nodes_per_cluster), random_state=42)
        self.workbench['cluster'] = kmeans.fit_predict(embeddings)
        self.cluster_nodes = self.workbench.groupby('cluster')['Label'].apply(list).to_dict()

        umap_coords_list = []
        for cluster_id in self.workbench['cluster'].unique():
            cluster_data = self.workbench[self.workbench['cluster'] == cluster_id]
            umap_model = UMAP(n_neighbors=min(50, len(cluster_data)-1), min_dist=0.1, n_components=2, metric='cosine')
            umap_coords = pd.DataFrame(umap_model.fit_transform(np.stack(cluster_data['embedding'])), columns=['x', 'y'])
            umap_coords['cluster'] = cluster_id
            umap_coords['Label'] = cluster_data['Label'].values
            umap_coords['ViewCount'] = cluster_data['ViewCount'].values
            umap_coords_list.append(umap_coords)
        
        self.umap_coords = pd.concat(umap_coords_list, ignore_index=True)
        self.umap_coords[['x', 'y']] *= 1000  # Scale UMAP coordinates by 1000

        self.workbench = pd.merge(self.workbench, self.umap_coords, on=['Label', 'cluster'], how='left')
        self.workbench['z'] = self.workbench['NormalizedViewCount']  # Add normalized view counts as z-coordinate

        self.assign_cluster_names_with_chatgpt()
        self.space_out_clusters_and_points()
        self.extract_mst_data()
        self.label_clusters()

    def assign_cluster_names_with_chatgpt(self):
        self.cluster_names = {}
        for cluster_id, labels in self.cluster_nodes.items():
            context = " ".join(labels)
            label = self.generate_label_with_chatgpt(context)
            self.cluster_names[cluster_id] = label

    def generate_label_with_chatgpt(self, context):
        response = openai.ChatCompletion.create(
            model="gpt-3.5-turbo",
            messages=[
                {"role": "system", "content": f"Label a cluster in a few words based off of this content: {context}"}
            ]
        )
        label = response.choices[0].message['content'].strip()
        print(label)
        return label

    def load_skills_data_from_csv(self, csv_file):
        df = pd.read_csv(csv_file, encoding='utf-8')
        print(f"Dataframe shape: {df.shape}")

        column_array = []
        final_array = []
        counter2 = 0
        for column in ['Description', 'Transcript']:
            column_data = df[column].tolist()
            print(f"Column '{column}' length: {len(column_data)}")
            column_array.append(column_data)

        for array in column_array:
            counter = 0
            for node in array:
                if counter2 == 0:
                    final_array.append(str(node))
                else:
                    final_array[counter] = final_array[counter] + str(node)
                counter += 1
            counter2 += 1

        if not final_array:
            print("Error: The final array is empty after removing invalid entries.")
            return

        try:
            embeddings = get_embeddings_batch(final_array)
        except Exception as e:
            print(f"Error getting embeddings: {e}")
            return

        label_list = df['Title'].tolist()
        self.workbench = pd.DataFrame({'Label': label_list, 'embedding': list(embeddings)})
        self.workbench['ViewCount'] = df['ViewCount'].str.replace(',', '').astype(float)  # Clean and convert ViewCount to float

    def create_connections(self):
        unique_clusters = set(self.workbench['cluster'])
        connections = {'FirstPair': [], 'SecondPair': []}

        for cluster in unique_clusters:
            cluster_points = self.workbench[self.workbench['cluster'] == cluster]['embedding'].tolist()
            if len(cluster_points) < 2:
                continue

            max_similarity = -np.inf
            best_pair = (None, None)

            for i in range(len(cluster_points)):
                for j in range(i + 1, len(cluster_points)):
                    similarity = cosine_similarity(cluster_points[i], cluster_points[j])
                    if similarity > max_similarity:
                        max_similarity = similarity
                        best_pair = (cluster_points[i], cluster_points[j])

            if best_pair[0] is not None and best_pair[1] is not None:
                connections['FirstPair'].append(best_pair[0])
                connections['SecondPair'].append(best_pair[1])

        self.connections = pd.DataFrame(connections)

    def create_minimum_spanning_trees(self):
        minimum_spanning_trees = {}

        for cluster_id in self.workbench['cluster'].unique():
            cluster_embeddings = self.workbench[self.workbench['cluster'] == cluster_id]['embedding']

            if not cluster_embeddings.empty:
                cluster_embeddings = np.stack(cluster_embeddings)
                pairwise_distances = cosine_distances(cluster_embeddings)
                mst = minimum_spanning_tree(pairwise_distances)
                edges = mst.nonzero()
                edges = np.vstack((edges[0], edges[1])).T
                minimum_spanning_trees[cluster_id] = edges

        return minimum_spanning_trees

    def space_out_clusters_and_points(self):
        centroids = self.workbench.groupby('cluster')[['x', 'y']].mean().values.astype(np.float64)
        max_iterations = 100
        learning_rate = 0.1
        min_distance = 5  # Reduced minimum distance between cluster centroids
        spread_scale = 0.5  # Scale for spreading out points within clusters

        for _ in range(max_iterations):
            for i in range(len(centroids)):
                for j in range(i + 1, len(centroids)):
                    delta = centroids[j] - centroids[i]
                    distance = np.linalg.norm(delta)
                    if distance < min_distance:
                        adjustment = (min_distance - distance) * delta / distance * learning_rate
                        centroids[i] -= adjustment
                        centroids[j] += adjustment

        for cluster_id, centroid in enumerate(centroids):
            cluster_points = self.workbench[self.workbench['cluster'] == cluster_id][['x', 'y']].values.astype(np.float64)
            cluster_mean = cluster_points.mean(axis=0)
            displacement = centroid - cluster_mean
            self.workbench.loc[self.workbench['cluster'] == cluster_id, ['x', 'y']] += displacement

            # Spread out points within the cluster more significantly
            self.workbench.loc[self.workbench['cluster'] == cluster_id, ['x', 'y']] += np.random.normal(scale=spread_scale, size=cluster_points.shape).astype(np.float64)

        # Ensure no overlapping
        for i in range(len(centroids)):
            for j in range(i + 1, len(centroids)):
                if self.check_overlap(i, j):
                    self.resolve_overlap(i, j)

    def check_overlap(self, cluster_id1, cluster_id2):
        points1 = self.workbench[self.workbench['cluster'] == cluster_id1][['x', 'y']].values
        points2 = self.workbench[self.workbench['cluster'] == cluster_id2][['x', 'y']].values

        for p1 in points1:
            for p2 in points2:
                if np.linalg.norm(p1 - p2) < self.min_nodes_per_cluster:
                    return True
        return False

    def resolve_overlap(self, cluster_id1, cluster_id2):
        displacement = np.random.normal(scale=1.0, size=(2,)).astype(np.float64)
        self.workbench.loc[self.workbench['cluster'] == cluster_id2, ['x', 'y']] += displacement

    def normalize_view_count(self):
        scaler = MinMaxScaler(feature_range=(5, 60))
        self.workbench['NormalizedViewCount'] = scaler.fit_transform(self.workbench[['ViewCount']])

    def extract_mst_data(self):
        self.mst_data = self.create_minimum_spanning_trees()

    def label_clusters(self):
        unique_clusters = self.workbench['cluster'].unique()
        cluster_label_mapping = {cluster: idx + 1 for idx, cluster in enumerate(unique_clusters)}
        self.workbench['ClusterLabel'] = self.workbench['cluster'].map(cluster_label_mapping)

    def save_to_files(self):
        self.workbench[['x', 'y', 'z', 'cluster', 'Label']].to_csv('umap_coordinates.csv', index=False)  # Include z-coordinate, cluster label, and video label
        self.workbench[['ClusterLabel']].to_csv('cluster_labels.csv', index=False)
        mst_edges = []
        for cluster_id, edges in self.mst_data.items():
            for edge in edges:
                mst_edges.append([cluster_id, edge[0], edge[1]])
        mst_df = pd.DataFrame(mst_edges, columns=['ClusterID', 'Point1', 'Point2'])
        mst_df.to_csv('mst_data.csv', index=False)

    def plot_clusters_and_connections_with_mst(self):
        all_embeddings = self.workbench[['x', 'y']].values

        unique_clusters = self.workbench['cluster'].unique()
        cluster_labels = {cluster: label for label, cluster in enumerate(unique_clusters)}

        colors = [cluster_labels[cluster] for cluster in self.workbench['cluster']]
        plt.scatter(all_embeddings[:, 0], all_embeddings[:, 1], c=colors, cmap='viridis', alpha=0.5)

        for cluster_id in unique_clusters:
            cluster_indices = self.workbench[self.workbench['cluster'] == cluster_id].index
            cluster_coords = all_embeddings[cluster_indices]

            pairwise_distances = cosine_distances(cluster_coords)
            mst = minimum_spanning_tree(pairwise_distances)
            edges = mst.nonzero()
            edges = np.vstack((edges[0], edges[1])).T

            for edge in edges:
                start_point = cluster_coords[edge[0]]
                end_point = cluster_coords[edge[1]]
                plt.plot([start_point[0], end_point[0]], [start_point[1], end_point[1]], color='green')

            if len(cluster_coords) >= 3:
                hull = scipy.spatial.ConvexHull(cluster_coords)
                x_hull = np.append(cluster_coords[hull.vertices, 0], cluster_coords[hull.vertices[0], 0])
                y_hull = np.append(cluster_coords[hull.vertices, 1], cluster_coords[hull.vertices[0], 1])
                plt.plot(x_hull, y_hull, color='gray', linestyle='--', linewidth=1)

        plt.title('Clusters and Connections with Minimum Spanning Trees')
        plt.show()

# Example usage
if __name__ == "__main__":
    load_dotenv()
    api_key = os.getenv("apikey")
    csv_file = "chemistry2"
    max_cluster_depth = 2
    min_nodes = 10

    cluster_creator = ClusterCreator(max_cluster_depth, min_nodes)
    cluster_creator.load_skills_data_from_csv(csv_file)
    cluster_creator.make_clusters()
    cluster_creator.create_connections()
    cluster_creator.plot_clusters_and_connections_with_mst()

    # Save extracted data to files
    cluster_creator.save_to_files()
