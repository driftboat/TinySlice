using Unity.Entities;
using Unity.Mathematics;

namespace Slice
{
    [GenerateAuthoringComponent]
    public struct Plane : IComponentData
    {
        public float3 normal;
        public float dist;
        public bool exist;
		public float3 pos;
    }
}