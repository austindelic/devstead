using UnityEngine;

namespace Oceana {
    internal static class OceanaShaderIds {
        public static readonly int ScrollArrayST = Shader.PropertyToID("_ScrollArray_ST");
        public static readonly int ScrollCount = Shader.PropertyToID("_ScrollCount");
        public static readonly int Time = Shader.PropertyToID("_Time");
        public static readonly int ScrollArray = Shader.PropertyToID("_ScrollArray");
        public static readonly int ScrollResolution = Shader.PropertyToID("_ScrollResolution");
        public static readonly int MipLevel = Shader.PropertyToID("_MipLevel");
        public static readonly int ScrollMap = Shader.PropertyToID("_ScrollMap");
        public static readonly int ScrollMapST = Shader.PropertyToID("_ScrollMap_ST");
        public static readonly int SeaLevel = Shader.PropertyToID("_SeaLevel");
        public static readonly int DisplaceHeight = Shader.PropertyToID("_DisplaceHeight");
        public static readonly int SourceColor = Shader.PropertyToID("_SourceColor");
        public static readonly int SourceDepth = Shader.PropertyToID("_SourceDepth");
    }
}
