import pandas as pd
import fastcluster
from scipy.cluster import hierarchy as sch
import numpy as np
import uuid
import os
import openai
import matplotlib.pyplot as plt
import umap.umap_ as umap
from scipy.sparse.csgraph import minimum_spanning_tree
import scipy
from sklearn.manifold import TSNE
import matplotlib.cm as cm
from adjustText import adjust_text
from openai import OpenAI

# Load OpenAI API key from environment variable for security

def get_embeddings_batch(inputs):
    embeddings = []
    for input_text in inputs:
        response = client.embeddings.create(
            input=input_text,
            model="text-embedding-3-large"
        )
        embedding = np.array(response.data[0].embedding)
        embeddings.append(embedding)
    return np.array(embeddings)

def cosine_similarity(a, b):
    dot_product = np.dot(a, b)
    norm_a = np.linalg.norm(a)
    norm_b = np.linalg.norm(b)
    return dot_product / (norm_a * norm_b)

class ClusterCreator:
    def __init__(self, max_cluster_depth, min_nodes_per_cluster):
        self.max_cluster_depth = max_cluster_depth
        self.min_nodes_per_cluster = min_nodes_per_cluster
        self.cluster_list = []
        self.workbench = None
        self.skill_cluster_mapping = {}
        self.cluster_nodes = {}
        self.connections = None
        self.umap_coords = None
        self.cluster_names = {}  # Dictionary to store cluster names

    def make_clusters(self):
        linkage = fastcluster.linkage_vector(self.workbench['embedding'].to_list(), method='ward', metric='euclidean')
        clusterTree = sch.to_tree(linkage, True)

        cluster_list = []

        def add_node_to_clusters(node, parent_id=None, depth=0):
            if node.is_leaf():
                original_data_index = node.get_id()
                label = self.workbench.at[original_data_index, 'Label']
                self.skill_cluster_mapping[label] = parent_id
                if parent_id not in self.cluster_nodes:
                    self.cluster_nodes[parent_id] = []
                self.cluster_nodes[parent_id].append(label)
            else:
                if depth == self.max_cluster_depth:
                    cluster_id = uuid.uuid4()
                    cluster_dict = {'id': cluster_id, 'parent_id': parent_id, 'depth': depth, 'children': [], 'center': None, 'color': None}
                    cluster_list.append(cluster_dict)
                    left_child_id = add_node_to_clusters(node.get_left(), parent_id=cluster_id, depth=depth+1)
                    right_child_id = add_node_to_clusters(node.get_right(), parent_id=cluster_id, depth=depth+1)
                    if left_child_id is not None:
                        cluster_dict['children'].append(left_child_id)
                    if right_child_id is not None:
                        cluster_dict['children'].append(right_child_id)
                    return cluster_id
                else:
                    left_cluster_id = add_node_to_clusters(node.get_left(), parent_id=parent_id, depth=depth+1)
                    right_cluster_id = add_node_to_clusters(node.get_right(), parent_id=parent_id, depth=depth+1)

                    # Merge clusters if they don't meet the minimum node requirement
                    if left_cluster_id is not None or right_cluster_id is not None:
                        if parent_id is not None:
                            left_cluster_nodes = self.cluster_nodes.get(left_cluster_id, [])
                            right_cluster_nodes = self.cluster_nodes.get(right_cluster_id, [])
                            total_nodes = len(left_cluster_nodes) + len(right_cluster_nodes)

                            if total_nodes < self.min_nodes_per_cluster:
                                if left_cluster_id is not None:
                                    self.merge_cluster(parent_id, left_cluster_id)
                                if right_cluster_id is not None:
                                    self.merge_cluster(parent_id, right_cluster_id)
                                return parent_id

                    return parent_id

        add_node_to_clusters(clusterTree[0])
        self.clusters = pd.DataFrame(cluster_list)
        self.clusters.set_index('id', inplace=True)
        self.workbench['cluster'] = self.workbench['Label'].map(self.skill_cluster_mapping)
        umap_model = umap.UMAP(n_neighbors=50, min_dist=0.1, n_components=2, metric='cosine')
        embedding_array = np.stack(self.workbench['embedding'])
        self.umap_coords = pd.Series(umap_model.fit_transform(embedding_array).tolist())
        self.workbench['umap_coords'] = self.umap_coords

        self.assign_cluster_names_with_chatgpt()

    def merge_cluster(self, parent_id, child_id):
        child_nodes = self.cluster_nodes.pop(child_id, [])

        if parent_id in self.cluster_nodes:
            self.cluster_nodes[parent_id].extend(child_nodes)
        else:
            self.cluster_nodes[parent_id] = child_nodes

        # Update self.skill_cluster_mapping
        for label in child_nodes:
            self.skill_cluster_mapping[label] = parent_id

        # Update self.workbench['cluster']
        self.workbench.loc[self.workbench['Label'].isin(child_nodes), 'cluster'] = parent_id
    def assign_cluster_names_with_chatgpt(self):
        self.cluster_names = {}
        for cluster_id, labels in self.cluster_nodes.items():
            # Prepare context for ChatGPT
            context = " ".join(labels)
            # Generate label using ChatGPT
            label = self.generate_label_with_chatgpt(context)
            self.cluster_names[cluster_id] = label

    def generate_label_with_chatgpt(self, context):
        label = client.chat.completions.create(
            model="gpt-3.5-turbo",
            messages=[
                {"role": "system", "content": f"Label a cluster in a few words based off of this content {context}?"}
            ]
        )

        print(label.choices[0].message.content)
        return label.choices[0].message.content

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

    def create_connections(self):
        unique_clusters = set(self.workbench['cluster'])
        connections = {'FirstPair': [], 'SecondPair': []}

        for cluster in unique_clusters:
            cluster_points = self.workbench[self.workbench['cluster'] == cluster]['embedding']

            max_similarity = -np.inf
            best_pair = (None, None)

            for other_cluster in unique_clusters:
                if other_cluster == cluster:
                    continue
                    
                other_cluster_points = self.workbench[self.workbench['cluster'] == other_cluster]['embedding']

                for point1 in cluster_points:
                    for point2 in other_cluster_points:
                        similarity = cosine_similarity(point1, point2)
                        if similarity > max_similarity:
                            max_similarity = similarity
                            best_pair = (point1, point2)

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
                pairwise_distances = np.linalg.norm(cluster_embeddings[:, None] - cluster_embeddings, axis=-1)
                mst = minimum_spanning_tree(pairwise_distances)
                edges = mst.nonzero()
                edges = np.vstack((edges[0], edges[1])).T
                minimum_spanning_trees[cluster_id] = edges

        return minimum_spanning_trees

    def plot_clusters_and_connections_with_mst(self):
        embedding_array = np.stack(self.workbench['embedding'])
        umap_model = umap.UMAP(n_neighbors=50, min_dist=0.1, n_components=2, metric='cosine')
        all_embeddings = umap_model.fit_transform(embedding_array)

        unique_clusters = self.workbench['cluster'].unique()
        cluster_labels = {cluster: label for label, cluster in enumerate(unique_clusters)}

        colors = [cluster_labels[cluster] for cluster in self.workbench['cluster']]
        plt.scatter(all_embeddings[:, 0], all_embeddings[:, 1], c=colors, cmap='viridis', alpha=0.5)

        for cluster_id in unique_clusters:
            cluster_indices = self.workbench[self.workbench['cluster'] == cluster_id].index
            cluster_coords = all_embeddings[cluster_indices]

            pairwise_distances = scipy.spatial.distance.squareform(scipy.spatial.distance.pdist(cluster_coords))
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

            # Annotate the cluster with its name at the centroid
            cluster_name = self.cluster_names.get(cluster_id, 'Unknown')
            centroid = np.mean(cluster_coords, axis=0)
            plt.text(centroid[0], centroid[1], cluster_name, fontsize=10, fontweight='bold', ha='center')

        for i in range(len(self.connections['FirstPair'])):
            first_pair_idx = np.where(np.all(embedding_array == self.connections['FirstPair'][i], axis=1))[0][0]
            second_pair_idx = np.where(np.all(embedding_array == self.connections['SecondPair'][i], axis=1))[0][0]
            first_cluster_id = self.workbench.loc[first_pair_idx, 'cluster']
            second_cluster_id = self.workbench.loc[second_pair_idx, 'cluster']

            closest_pair = None
            min_distance = np.inf

            for point1 in all_embeddings[self.workbench[self.workbench['cluster'] == first_cluster_id].index]:
                for point2 in all_embeddings[self.workbench[self.workbench['cluster'] == second_cluster_id].index]:
                    distance = np.linalg.norm(point1 - point2)
                    if distance < min_distance:
                        min_distance = distance
                        closest_pair = (point1, point2)

            if closest_pair is not None:
                start_point, end_point = closest_pair
                plt.plot([start_point[0], end_point[0]], [start_point[1], end_point[1]], color='purple', alpha=0.5)

        texts = []
        for i, labels in enumerate(self.workbench['Label']):
            text = plt.annotate(labels, (all_embeddings[i, 0], all_embeddings[i, 1]), fontsize=4, alpha=0.7, fontweight='bold')
            texts.append(text)

        plt.title('Clusters and Connections with Minimum Spanning Trees')
        plt.legend()
        plt.show()

# Example usage
client = OpenAI(api_key="apikey")
csv_file = "chemistry.csv"
max_cluster_depth = 2
min_nodes= 10

cluster_creator = ClusterCreator(max_cluster_depth, min_nodes)
cluster_creator.load_skills_data_from_csv(csv_file)
cluster_creator.make_clusters()
cluster_creator.create_connections()
cluster_creator.plot_clusters_and_connections_with_mst()
