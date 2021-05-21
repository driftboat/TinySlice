using Unity.Entities;
using Unity.Tiny.Rendering; 

namespace Slice
{ 
    public struct DynamicTriangle : IBufferElementData
    {
        public LitTriangle triangle; 
    }
    
    public struct LitTriangle{
        public LitVertex vertexA;
        public LitVertex vertexB;
        public LitVertex vertexC;
    }
    
}
