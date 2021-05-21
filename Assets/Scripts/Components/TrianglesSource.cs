using Unity.Entities; 

namespace Slice
{
    [GenerateAuthoringComponent]
    public struct TrianglesSource : IComponentData
    {
        public Entity sourceEntity; 
    }
    
}