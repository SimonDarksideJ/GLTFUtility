using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Siccity.GLTFUtility.WebRequestRest;
using UnityEngine;

namespace Siccity.GLTFUtility {
	/// <summary> API used for importing .gltf and .glb files </summary>
	public static class WebImporter {
		public async static Task<GameObject> LoadFromURI(string path, Format format = Format.AUTO) {
			return await LoadFromURI(path, new ImportSettings(), format);
		}

		public async static Task<GameObject> LoadFromUri(string path, ImportSettings importSettings, Format format = Format.AUTO) {
			return await LoadFromURI(path, importSettings, format);
		}

		public async static Task<GameObject> LoadFromURI(string path, ImportSettings importSettings, Format format = Format.AUTO) {
			if (format == Format.GLB) {
				return ImportGLB(path, importSettings);
			} else if (format == Format.GLTF) {
				return await WebImportGLTF(path, importSettings);
			} else {
				string extension = importSettings.localFiles ? Path.GetExtension(path).ToLower() : path.Substring(path.Length - 4, 4);
				if (extension == ".glb") return ImportGLB(path, importSettings);
				else if (extension == ".gltf") return await WebImportGLTF(path, importSettings);
				else {
					Debug.Log("Extension '" + extension + "' not recognized in " + path);
					return null;
				}
			}
		}

#region GLB
		private static GameObject ImportGLB(string path, ImportSettings importSettings) {
			FileStream stream = File.OpenRead(path);
			long binChunkStart;
			string json = GetGLBJson(stream, out binChunkStart);
			GLTFObject gltfObject = JsonConvert.DeserializeObject<GLTFObject>(json);
			return gltfObject.WebLoadInternal(path, null, binChunkStart, importSettings);
		}

		private static GameObject ImportGLB(byte[] bytes, ImportSettings importSettings) {
			Stream stream = new MemoryStream(bytes);
			long binChunkStart;
			string json = GetGLBJson(stream, out binChunkStart);
			GLTFObject gltfObject = JsonConvert.DeserializeObject<GLTFObject>(json);
			return gltfObject.WebLoadInternal(null, bytes, binChunkStart, importSettings);
		}

		private static string GetGLBJson(Stream stream, out long binChunkStart) {
			byte[] buffer = new byte[12];
			stream.Read(buffer, 0, 12);
			// 12 byte header
			// 0-4  - magic = "glTF"
			// 4-8  - version = 2
			// 8-12 - length = total length of glb, including Header and all Chunks, in bytes.
			string magic = Encoding.Default.GetString(buffer, 0, 4);
			if (magic != "glTF") {
				Debug.LogWarning("Input does not look like a .glb file");
				binChunkStart = 0;
				return null;
			}
			uint version = System.BitConverter.ToUInt32(buffer, 4);
			if (version != 2) {
				Debug.LogWarning("Importer does not support gltf version " + version);
				binChunkStart = 0;
				return null;
			}
			// What do we even need the length for.
			//uint length = System.BitConverter.ToUInt32(bytes, 8);

			// Chunk 0 (json)
			// 0-4  - chunkLength = total length of the chunkData
			// 4-8  - chunkType = "JSON"
			// 8-[chunkLength+8] - chunkData = json data.
			stream.Read(buffer, 0, 8);
			uint chunkLength = System.BitConverter.ToUInt32(buffer, 0);
			TextReader reader = new StreamReader(stream);
			char[] jsonChars = new char[chunkLength];
			reader.Read(jsonChars, 0, (int) chunkLength);
			string json = new string(jsonChars);

			// Chunk
			binChunkStart = chunkLength + 20;
			stream.Close();

			// Return json
			return json;
		}
#endregion

		private async static Task<GameObject> WebImportGLTF(string path, ImportSettings importSettings) {
			if (importSettings.localFiles)
			{
				return null;
			}

			var response = await Rest.GetAsync(path, importSettings.headers);
			if (response.Successful)
			{
				// Parse json
				GLTFObject gltfObject = JsonConvert.DeserializeObject<GLTFObject>(response.ResponseBody);
				return gltfObject.WebLoadInternal(path, null, 0, importSettings);
			}

			return null;
		}

		public abstract class ImportTask<TReturn> : ImportTask {
			public TReturn Result;

			/// <summary> Constructor. Sets waitFor which ensures ImportTasks are completed before running. </summary>
			public ImportTask(params ImportTask[] waitFor) : base(waitFor) { }

			/// <summary> Runs task followed by OnCompleted </summary>
			public TReturn RunSynchronously() {
				task.RunSynchronously();
				IEnumerator en = OnCoroutine();
				while (en.MoveNext()) { };
				return Result;
			}
		}

		public abstract class ImportTask {
			public Task task;
			public readonly ImportTask[] waitFor;
			public bool IsReady { get { return waitFor.All(x => x.IsCompleted); } }
			public bool IsCompleted { get; protected set; }

			/// <summary> Constructor. Sets waitFor which ensures ImportTasks are completed before running. </summary>
			public ImportTask(params ImportTask[] waitFor) {
				IsCompleted = false;
				this.waitFor = waitFor;
			}

			public virtual IEnumerator OnCoroutine(Action<float> onProgress = null) {
				IsCompleted = true;
				yield break;
			}
		}

#region Sync
		private static GameObject WebLoadInternal(this GLTFObject gltfObject, string path, byte[] bytefile, long binChunkStart, ImportSettings importSettings) {
			// directory root is sometimes used for loading buffers from containing file, or local images
			string webRoot = !string.IsNullOrWhiteSpace(path) ? Directory.GetParent(path).ToString() + "/" : null;

			importSettings.shaderOverrides.CacheDefaultShaders();

			// Import tasks synchronously
			GLTFBuffer.ImportTask bufferTask = new GLTFBuffer.ImportTask(gltfObject.buffers, path, bytefile, binChunkStart);
			bufferTask.RunSynchronously();
			GLTFBufferView.ImportTask bufferViewTask = new GLTFBufferView.ImportTask(gltfObject.bufferViews, bufferTask);
			bufferViewTask.RunSynchronously();
			GLTFAccessor.ImportTask accessorTask = new GLTFAccessor.ImportTask(gltfObject.accessors, bufferViewTask);
			accessorTask.RunSynchronously();
			GLTFImage.ImportTask imageTask = new GLTFImage.ImportTask(gltfObject.images, webRoot, bufferViewTask);
			imageTask.RunSynchronously();
			GLTFTexture.ImportTask textureTask = new GLTFTexture.ImportTask(gltfObject.textures, imageTask);
			textureTask.RunSynchronously();
			GLTFMaterial.ImportTask materialTask = new GLTFMaterial.ImportTask(gltfObject.materials, textureTask, importSettings);
			materialTask.RunSynchronously();
			GLTFMesh.ImportTask meshTask = new GLTFMesh.ImportTask(gltfObject.meshes, accessorTask, materialTask, importSettings);
			meshTask.RunSynchronously();
			GLTFSkin.ImportTask skinTask = new GLTFSkin.ImportTask(gltfObject.skins, accessorTask);
			skinTask.RunSynchronously();
			GLTFNode.ImportTask nodeTask = new GLTFNode.ImportTask(gltfObject.nodes, meshTask, skinTask, gltfObject.cameras);
			nodeTask.RunSynchronously();
			var animations = gltfObject.animations.Import(accessorTask.Result, nodeTask.Result, importSettings);

			foreach (var item in bufferTask.Result) {
				item.Dispose();
			}

			return nodeTask.Result.GetRoot();
		}
#endregion
	}
}