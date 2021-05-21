using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Tiny;
using Unity.Tiny.Rendering;
using Unity.Transforms;

namespace Slice
{
	//collection triangles
    [UpdateInGroup(typeof(SimulationSystemGroup))] 
    [UpdateBefore(typeof(SliceSystem))] 
    public class GetTrianglesSystem : SystemBase
    { 
        protected override void OnUpdate()
        {
			
            var plane = GetSingleton<Plane>(); 
           	if (!plane.exist) return;
            Entities.ForEach((Entity entity,  in MeshRenderer meshRender, in LocalToWorld localToWorld) =>
            {
                var meshRenderData = EntityManager.GetComponentData<LitMeshRenderData>(meshRender.mesh);
               

                var gentries = EntityManager.CreateEntity();
                EntityManager.AddBuffer<DynamicTriangle>(gentries); 
				var tBuffer = EntityManager.GetBuffer<DynamicTriangle>(gentries);
                ref LitMeshData mesh = ref meshRenderData.Mesh.Value;
                ref BlobArray<LitVertex> verts = ref mesh.Vertices;
                ref BlobArray<ushort> indices = ref mesh.Indices;
                var indicesCount = indices.Length;   
                for (var index = 0; index < indicesCount; index += 3)
                {
                    int i0 = indices[index + 0];
                    int i1 = indices[index + 1];
                    int i2 = indices[index + 2];
                    var v0 = verts[i0];
                    var v1 = verts[i1];
                    var v2 = verts[i2];
                    v0.Position = math.transform(localToWorld.Value, v0.Position);
                    v1.Position = math.transform(localToWorld.Value, v1.Position);
                    v2.Position = math.transform(localToWorld.Value, v2.Position);
                    
                    v0.Normal = math.rotate(localToWorld.Value, v0.Normal);
                    v1.Normal = math.rotate(localToWorld.Value, v1.Normal);
                    v2.Normal = math.rotate(localToWorld.Value, v2.Normal);
                    
                    v0.Tangent = math.rotate(localToWorld.Value, v0.Tangent);
                    v1.Tangent = math.rotate(localToWorld.Value, v1.Tangent);
                    v2.Tangent = math.rotate(localToWorld.Value, v2.Tangent);
                    tBuffer.Add(new DynamicTriangle{triangle = new LitTriangle{vertexA = v0,vertexB = v1,vertexC = v2} });
                }

                EntityManager.AddComponent<TrianglesSource>(gentries);
                EntityManager.SetComponentData(gentries,new TrianglesSource()
                {
                    sourceEntity =  entity
                }); 

				//EntityManager.AddComponent<SliceChecked>(entity); 
            }).WithoutBurst().WithStructuralChanges().Run();  
            
        }
    }
}

