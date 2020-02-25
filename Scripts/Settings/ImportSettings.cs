using System;
using System.Collections.Generic;
using UnityEngine.Serialization;

namespace Siccity.GLTFUtility
{
	[Serializable]
	public class ImportSettings {
		public bool materials = true;
		[FormerlySerializedAs("shaders")]
		public ShaderSettings shaderOverrides = new ShaderSettings();
		public bool useLegacyClips;
		public bool localFiles = true;
		public bool relativePaths = true;
		public Dictionary<string, string> headers = new Dictionary<string, string>();
	}
}