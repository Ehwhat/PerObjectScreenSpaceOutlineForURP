using System;
using System.Collections.Generic;
using UnityEngine;

namespace EhLib.Outline
{
    [ExecuteAlways]
    public class Outline : MonoBehaviour
    {
        public static IReadOnlyList<Outline> Outlines => outlines;
        private static List<Outline> outlines = new List<Outline>();

        public IReadOnlyList<Renderer> Renderers => renderers;
        private List<Renderer> renderers = new List<Renderer>();
        
        public OutlineDefinition outlineDefinition;
        public float outlineAlpha = 1f;

        public void OnEnable()
        {
            UpdateRenderers();
            outlines.Add(this);
        }

        public void OnDisable()
        {
            outlines.Remove(this);
        }

        public void UpdateRenderers()
        {
            renderers.Clear();
            GetComponentsInChildren(renderers);
        }
    }
}