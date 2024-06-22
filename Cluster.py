import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
from sklearn.cluster import KMeans
from umap import UMAP
from scipy.sparse.csgraph import minimum_spanning_tree
from sklearn.metrics.pairwise import cosine_distances
from sklearn.preprocessing import MinMaxScaler
import openai
from dotenv import load_dotenv
import os
import networkx as nx
from sklearn.metrics.pairwise import euclidean_distances
# Load OpenAI API key from environment variable for security
load_dotenv()
api_key = os.getenv("apikey")
openai.api_key = api_key

def get_embeddings_batch(inputs):
    embeddings = []
    for input_text in inputs:
        response = openai.Embedding.create(
            input=input_text,
            model="text-embedding-3-large"
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
    def __init__(self, max_cluster_depth, min_nodes_per_cluster, min_clusters=2, max_clusters=5):
        self.max_cluster_depth = max_cluster_depth
        self.min_nodes_per_cluster = min_nodes_per_cluster
        self.min_clusters = min_clusters
        self.max_clusters = max_clusters
        self.cluster_list = []
        self.workbench = None
        self.skill_cluster_mapping = {}
        self.cluster_nodes = {}
        self.connections = None
        self.umap_coords = None
        self.cluster_names = {}
        self.mst_data = None

    def make_clusters(self):
        self.normalize_view_count()
        
        embeddings = np.stack(self.workbench['embedding'])
        n_clusters = min(max(self.min_clusters, len(self.workbench) // self.min_nodes_per_cluster), self.max_clusters)
        kmeans = KMeans(n_clusters=n_clusters, random_state=42)
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
        self.umap_coords[['x', 'y']] *= 400  # Scale UMAP coordinates by 100

        self.workbench = pd.merge(self.workbench, self.umap_coords, on=['Label', 'cluster'], how='left')
        self.workbench['z'] = self.workbench['NormalizedViewCount']  # Add normalized view counts as z-coordinate

        # Add the 'umap_coords' column to the DataFrame
        self.workbench['umap_coords'] = list(zip(self.workbench['x'], self.workbench['y']))

        self.assign_cluster_names()
        self.space_out_clusters_and_points()
        self.merge_overlapping_clusters()
        self.create_minimum_spanning_trees()  # Corrected method name
        self.label_clusters()

    def assign_cluster_names(self):
        self.cluster_names = {}
        for cluster_id, labels in self.cluster_nodes.items():
            context = " ".join(labels)
            self.cluster_names[cluster_id] = f"Cluster {cluster_id}"  # Simplified naming

    def load_skills_data_from_csv(self, csv_file):
        df = pd.read_csv(csv_file, encoding='utf-8')
        column_array = []
        final_array = []
        counter2 = 0
        for column in ['Description', 'Transcript']:
            column_data = df[column].tolist()
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

        embeddings = get_embeddings_batch(final_array)
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
        minimum_spanning_trees = []

        for cluster_id in self.workbench['cluster'].unique():
            cluster_data = self.workbench[self.workbench['cluster'] == cluster_id]
            cluster_coords = cluster_data[['x', 'y']].values

            if len(cluster_coords) > 1:
                pairwise_distances = euclidean_distances(cluster_coords)
                mst = minimum_spanning_tree(pairwise_distances)
                edges = mst.nonzero()

                for start, end in zip(edges[0], edges[1]):
                    start_node = cluster_data.iloc[start]['Label']
                    end_node = cluster_data.iloc[end]['Label']
                    minimum_spanning_trees.append([cluster_id, start_node, end_node])

        return pd.DataFrame(minimum_spanning_trees, columns=['ClusterID', 'StartNode', 'EndNode'])

    def save_mst_to_csv(self, output_file):
        mst_df = self.create_minimum_spanning_trees()
        mst_df.to_csv(output_file, index=False)
        print(f"Minimum Spanning Trees saved to {output_file}")

    def space_out_clusters_and_points(self):
        centroids = self.workbench.groupby('cluster')[['x', 'y']].mean().values.astype(np.float64)
        max_iterations = 100
        learning_rate = 0.1
        min_distance_between_clusters = .1  # Further reduced minimum distance between cluster centroids
        spread_scale_within_clusters = 100  # Increased scale for spreading out points within clusters

        for _ in range(max_iterations):
            for i in range(len(centroids)):
                for j in range(i + 1, len(centroids)):
                    delta = centroids[j] - centroids[i]
                    distance = np.linalg.norm(delta)
                    if distance < min_distance_between_clusters:
                        adjustment = (min_distance_between_clusters - distance) * delta / distance * learning_rate
                        centroids[i] -= adjustment
                        centroids[j] += adjustment

        for cluster_id, centroid in enumerate(centroids):
            cluster_points = self.workbench[self.workbench['cluster'] == cluster_id][['x', 'y']].values.astype(np.float64)
            cluster_mean = cluster_points.mean(axis=0)
            displacement = (centroid - cluster_mean).astype(np.float32)
            self.workbench.loc[self.workbench['cluster'] == cluster_id, ['x', 'y']] = (self.workbench.loc[self.workbench['cluster'] == cluster_id, ['x', 'y']].values + displacement).astype(np.float32)

            self.workbench.loc[self.workbench['cluster'] == cluster_id, ['x', 'y']] = (self.workbench.loc[self.workbench['cluster'] == cluster_id, ['x', 'y']].values + np.random.normal(scale=spread_scale_within_clusters, size=cluster_points.shape)).astype(np.float32)

    def merge_overlapping_clusters(self):
        centroids = self.workbench.groupby('cluster')[['x', 'y']].mean().values.astype(np.float64)
        cluster_ids = self.workbench['cluster'].unique()

        for i in range(len(cluster_ids)):
            for j in range(i + 1, len(cluster_ids)):
                if self.check_overlap(cluster_ids[i], cluster_ids[j]):
                    self.merge_clusters(cluster_ids[i], cluster_ids[j])

    def check_overlap(self, cluster_id1, cluster_id2):
        points1 = self.workbench[self.workbench['cluster'] == cluster_id1][['x', 'y']].values
        points2 = self.workbench[self.workbench['cluster'] == cluster_id2][['x', 'y']].values

        for p1 in points1:
            for p2 in points2:
                if np.linalg.norm(p1 - p2) < self.min_nodes_per_cluster:
                    return True
        return False

    def merge_clusters(self, cluster_id1, cluster_id2):
        self.workbench.loc[self.workbench['cluster'] == cluster_id2, 'cluster'] = cluster_id1

    def normalize_view_count(self):
        scaler = MinMaxScaler(feature_range=(5, 60))
        self.workbench['NormalizedViewCount'] = scaler.fit_transform(self.workbench[['ViewCount']])

    def label_clusters(self):
        unique_clusters = self.workbench['cluster'].unique()
        cluster_label_mapping = {cluster: idx + 1 for idx, cluster in enumerate(unique_clusters)}
        self.workbench['ClusterLabel'] = self.workbench['cluster'].map(cluster_label_mapping)

    def save_to_files(self):
        self.workbench[['x', 'y', 'z', 'cluster', 'Label']].to_csv('umap_coordinates.csv', index=False)
        self.workbench[['ClusterLabel']].to_csv('cluster_labels.csv', index=False)
   
    def plot_clusters_and_connections_with_mst(self):
        fig, ax = plt.subplots(figsize=(15, 10))

        unique_clusters = self.workbench['cluster'].unique()
        colors = plt.cm.rainbow(np.linspace(0, 1, len(unique_clusters)))

        for cluster_id, color in zip(unique_clusters, colors):
            cluster_data = self.workbench[self.workbench['cluster'] == cluster_id]
            cluster_coords = cluster_data[['x', 'y']].values

            # Create MST using 2D UMAP coordinates
            pairwise_distances = euclidean_distances(cluster_coords)
            mst = minimum_spanning_tree(pairwise_distances)
            edges = mst.nonzero()

            # Plot points
            ax.scatter(cluster_coords[:, 0], cluster_coords[:, 1], 
                    c=[color], s=50, alpha=0.6, label=f'Cluster {cluster_id}')

            # Plot MST edges
            for start, end in zip(edges[0], edges[1]):
                ax.plot([cluster_coords[start, 0], cluster_coords[end, 0]],
                        [cluster_coords[start, 1], cluster_coords[end, 1]],
                        c=color, alpha=0.5, linewidth=1.5)

        ax.set_xlabel('UMAP x-coordinate')
        ax.set_ylabel('UMAP y-coordinate')
        ax.legend(bbox_to_anchor=(1.05, 1), loc='upper left')
        plt.title('2D Visualization of Clusters with Minimum Spanning Trees')
        plt.tight_layout()
        plt.show()

# Example usage
if __name__ == "__main__":
    csv_file = "chemistry2"
    max_cluster_depth = 2
    min_nodes = 10

    cluster_creator = ClusterCreator(max_cluster_depth, min_nodes)
    cluster_creator.load_skills_data_from_csv(csv_file)
    cluster_creator.make_clusters()
    cluster_creator.create_connections()
    cluster_creator.plot_clusters_and_connections_with_mst()
    cluster_creator.save_mst_to_csv("mst_data.csv")
    cluster_creator.save_to_files()
