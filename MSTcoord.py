import pandas as pd
import numpy as np
import os
import openai
from dotenv import load_dotenv
from umap import UMAP
import fastcluster
from scipy.cluster import hierarchy as sch
from scipy.sparse.csgraph import minimum_spanning_tree
import uuid

def get_embeddings_batch(inputs):
    embeddings = []
    for input_text in inputs:
        try:
            response = openai.Embedding.create(
                input=input_text,
                model="text-embedding-3-large"
            )
            embedding = np.array(response['data'][0]['embedding'])
            embeddings.append(embedding)
        except Exception as e:
            print(f"Error getting embedding for input: {input_text[:30]}... Error: {e}")
            embeddings.append(None)
    return embeddings

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

    def load_skills_data_from_csv(self, csv_file):
        try:
            df = pd.read_csv(csv_file, encoding='utf-8')
            print(f"Dataframe shape: {df.shape}")
        except Exception as e:
            print(f"Error reading CSV file: {e}")
            return

        column = 'Transcript'
        if column not in df.columns:
            print(f"Error: Column '{column}' not found in the CSV file.")
            return

        column_data = df[column].tolist()
        final_array = [str(node) for node in column_data if str(node).strip()]
        if not final_array:
            print("Error: The final array is empty after removing invalid entries.")
            return

        try:
            embeddings = get_embeddings_batch(final_array)
            embeddings, final_array = zip(*[(e, f) for e, f in zip(embeddings, final_array) if e is not None])
        except Exception as e:
            print(f"Error getting embeddings: {e}")
            return

        label_list = df['Title'].tolist()
        view_count_list = df['ViewCount'].tolist()
        if len(label_list) != len(embeddings):
            print(f"Error: Label list length ({len(label_list)}) does not match embeddings length ({len(embeddings)}).")
            return

        self.workbench = pd.DataFrame({'Label': label_list, 'embedding': list(embeddings), 'ViewCount': view_count_list})

    def make_clusters(self):
        if self.workbench is None or self.workbench.empty:
            print("Error: Workbench data is not loaded or empty.")
            return

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

        umap_model = UMAP(n_neighbors=50, min_dist=0.1, n_components=2, metric='cosine')
        embedding_array = np.stack(self.workbench['embedding'])
        umap_coords = umap_model.fit_transform(embedding_array) * 500
        self.umap_coords = pd.Series(umap_coords.tolist())
        self.workbench['umap_coords'] = self.umap_coords

    def merge_cluster(self, parent_id, child_id):
        child_nodes = self.cluster_nodes.pop(child_id, [])

        if parent_id in self.cluster_nodes:
            self.cluster_nodes[parent_id].extend(child_nodes)
        else:
            self.cluster_nodes[parent_id] = child_nodes

        for label in child_nodes:
            self.skill_cluster_mapping[label] = parent_id

        self.workbench.loc[self.workbench['Label'].isin(child_nodes), 'cluster'] = parent_id

    def create_minimum_spanning_trees(self):
        minimum_spanning_trees = []

        for cluster_id in self.workbench['cluster'].unique():
            cluster_indices = self.workbench[self.workbench['cluster'] == cluster_id].index
            if len(cluster_indices) == 0:
                continue
            cluster_embeddings = self.workbench.loc[cluster_indices, 'embedding']
            cluster_coords = np.stack(self.workbench.loc[cluster_indices, 'umap_coords'])

            if not cluster_embeddings.empty:
                pairwise_distances = np.linalg.norm(cluster_coords[:, None] - cluster_coords, axis=-1)
                mst = minimum_spanning_tree(pairwise_distances)
                edges = mst.nonzero()
                edges = np.vstack((edges[0], edges[1])).T

                for edge in edges:
                    start_node = self.workbench.loc[cluster_indices[edge[0]], 'Label']
                    end_node = self.workbench.loc[cluster_indices[edge[1]], 'Label']
                    minimum_spanning_trees.append([cluster_id, start_node, end_node])

        return pd.DataFrame(minimum_spanning_trees, columns=['Cluster', 'StartNode', 'EndNode'])

    def save_mst_to_csv(self, output_file):
        mst_df = self.create_minimum_spanning_trees()
        mst_df.to_csv(output_file, index=False)
        print(f"Minimum Spanning Trees saved to {output_file}")

    def save_umap_coords_to_csv(self, output_file):
        umap_coords_df = self.workbench[['Label', 'umap_coords']].copy()
        umap_coords_df[['x', 'y']] = pd.DataFrame(umap_coords_df['umap_coords'].tolist(), index=umap_coords_df.index)
        umap_coords_df.drop('umap_coords', axis=1, inplace=True)

        # Clean and convert view counts to numeric values
        self.workbench['ViewCount'] = self.workbench['ViewCount'].str.replace(',', '').str.replace('"', '')
        view_counts = pd.to_numeric(self.workbench['ViewCount'], errors='coerce')
        
        min_view_count = view_counts.min()
        max_view_count = view_counts.max()
        
        if min_view_count is None or max_view_count is None or min_view_count == max_view_count:
            print("Error: Invalid view count data for normalization.")
            return

        def normalize_view_count(view_count, min_view, max_view):
            return -10 + (view_count - min_view) * (110 / (max_view - min_view))
        
        umap_coords_df['z'] = [normalize_view_count(vc, min_view_count, max_view_count) for vc in view_counts]

        print(umap_coords_df.head())  # Debugging line to print the first few rows of the DataFrame
        umap_coords_df.to_csv(output_file, index=False)
        print(f"UMAP coordinates saved to {output_file}")

# Example usage
if __name__ == "__main__":
    load_dotenv()
    api_key = os.getenv("apikey")
    if not api_key:
        print("API key not found. Make sure to set it in the .env file")
    else:
        openai.api_key = api_key
        csv_file = "chemistry2"  # Ensure the file name matches your actual CSV file
        mst_output_file = "mst_edges.csv"
        umap_output_file = "umap_coords.csv"
        max_cluster_depth = 2
        min_nodes = 10
        cluster_creator = ClusterCreator(max_cluster_depth, min_nodes)
        cluster_creator.load_skills_data_from_csv(csv_file)
        cluster_creator.make_clusters()
        cluster_creator.save_mst_to_csv(mst_output_file)
        cluster_creator.save_umap_coords_to_csv(umap_output_file)
