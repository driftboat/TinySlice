using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Tiny.Rendering;
using Unity.Collections;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;

namespace Slice
{
    //--create new entity from triangles
    [UpdateBefore(typeof(RenderGraphBuilder))]
    public class ComposeSystem : SystemBase
    {
        private Random random;
        Entity eCurrentMaterial;
        private bool created;

        protected override void OnUpdate()
        {
            if (!created)
            {
                var ePlainMaterial = EntityManager.CreateEntity();
                EntityManager.AddComponentData(ePlainMaterial, new LitMaterial
                {
                    texAlbedoOpacity = Entity.Null,
                    texMetal = Entity.Null,
                    texNormal = Entity.Null,
                    texEmissive = Entity.Null,
                    constEmissive = new float3(0),
                    constOpacity = 1.0f,
                    constAlbedo = new float3(1),
                    constMetal = 0.0f,
                    constSmoothness = .18f,
                    normalMapZScale = 1.0f,
                    twoSided = false,
                    transparent = false,
                    scale = new float2(1, 1),
                    offset = new float2(0, 0)
                });

                eCurrentMaterial = ePlainMaterial;
                created = true;
            }


            var plane = GetSingleton<Plane>();
            if (!plane.exist) return;
            float3 pos = plane.pos;
            Entities.ForEach((Entity entity, /* ref SliceSource sliceSource,*/ in DynamicBuffer<DynamicTriangle> dt) =>
            {
                //--create   entity
                var na = dt.AsNativeArray().Reinterpret<DynamicTriangle, LitTriangle>();
                var composeEntity = EntityManager.CreateEntity();
                if (na.Length < 3)
                {
                    Console.WriteLine("========"+na.Length);
                }

                //--create  mesh
                var vertextCount = na.Length * 3;
                LitMeshRenderData lmrd;
                var builder = new BlobBuilder(Allocator.Temp);
                ref var root = ref builder.ConstructRoot<LitMeshData>();
                var vertices = builder.Allocate(ref root.Vertices, vertextCount);
                var indices = builder.Allocate(ref root.Indices, vertextCount);
                MeshBounds mb;
                var centerPos = CreateMeshFromTriangles(na,
                    vertices.AsNativeArray(),
                    indices.AsNativeArray(),
                    out mb.Bounds);
  //      MeshHelper.ComputeNormals(vertices.AsNativeArray(),indices.AsNativeArray());
 //             MeshHelper.SetAlbedoColor(vertices.AsNativeArray(), new float4(1));
//                MeshHelper.SetMetalSmoothness(vertices.AsNativeArray(), new float2(1));
                lmrd.Mesh = builder.CreateBlobAssetReference<LitMeshData>(Allocator.Persistent);
                builder.Dispose();


                EntityManager.AddComponentData(composeEntity, lmrd);
                int indexCount = lmrd.Mesh.Value.Indices.Length;
                EntityManager.AddComponentData(composeEntity, mb);
                EntityManager.AddComponentData<MeshRenderer>(composeEntity, new MeshRenderer
                {
                    mesh = composeEntity,
                    material = eCurrentMaterial,
                    startIndex = 0,
                    indexCount = indexCount
                });
                EntityManager.AddComponentData(composeEntity, new LitMeshRenderer());
                EntityManager.AddComponentData(composeEntity, new LocalToWorld
                {
                    Value = float4x4.identity
                });
                EntityManager.AddComponentData(composeEntity, new Translation
                {
                    Value = centerPos
                });
                EntityManager.AddComponentData(composeEntity, new Rotation
                {
                    Value = quaternion.identity
                });

                EntityManager.AddComponentData(composeEntity,new DemoSpinner {spin = math.normalize(new quaternion(0, .2f, .1f, 1))});


                //--destroy
                EntityManager.DestroyEntity(entity);
            }).WithoutBurst().WithStructuralChanges().Run();

            plane.exist = false;
            SetSingleton(plane);
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            random = new Unity.Mathematics.Random(23);
        }
 
        static float3 CreateMeshFromTriangles(NativeArray<LitTriangle> triangles, NativeArray<LitVertex> destVertices,
            NativeArray<ushort> destIndices, out AABB bb)
        {
            int n = triangles.Length;
            int index = 0;
            for (int i = 0; i < n; i++)
            {
                var triangle = triangles[i];
                int v0 = index;
                int v1 = index + 1;
                int v2 = index + 2;
                destVertices[v0] = triangles[i].vertexA;
                destVertices[v1] = triangles[i].vertexB;
                destVertices[v2] = triangles[i].vertexC;

                destIndices[v0] = (ushort) v0;
                destIndices[v1] = (ushort) v1;
                destIndices[v2] = (ushort) v2;
                index += 3;
            }

            bb = MeshHelper.ComputeBounds(destVertices);
            var center = bb.Center;
            for (int i = 0; i < destVertices.Length; i++)
            {
                var vert = destVertices[i];
                vert.Position -= center;
                destVertices[i] = vert;
            }
            bb = MeshHelper.ComputeBounds(destVertices);
            return center;
        }
    }
}