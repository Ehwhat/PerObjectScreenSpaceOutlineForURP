using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace EhLib.Outline
{
    [CreateAssetMenu(menuName = "Ehlib/Outline Definition", fileName = "OutlineDefinition")]
    public class OutlineDefinition : ScriptableObject
    {
        
        [field: SerializeField, Tooltip("Material used for the outline. Leave empty to skip rendering the outline.")]
        public Material outlineMaterial;
        [field: SerializeField, Tooltip("Material used for the infill. Leave empty to skip rendering the infill.")]
        public Material infillMaterial;
        [field: SerializeField, Tooltip("Width of the outline.")]
        public float outlineWidth = 5f;
        [field: SerializeField, Tooltip("Whether to use the scene depth for occlusion.")]
        public bool useSceneDepthForOcclusion = true;

    }
}
