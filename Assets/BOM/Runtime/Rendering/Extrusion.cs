using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BOM
{
    public static class ObjectLifetime
    {
        public static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
    public static class Extrusion
    {
        public static BodyState PreviewState(PieceDraft draft) => new BodyState { region=draft.profile.Clone(),thickness=draft.thickness,
            uvU=draft.appearance.u,uvV=draft.appearance.v,uvOffset=draft.appearance.offset };
        public static Mesh Build(BodyState body)
        {
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();
            var indices=new[] {new List<int>(),new List<int>(),new List<int>()};float depth=body.thickness*.5f;
            int Vertex(Vector2 p,float z,Vector3 normal,Vector2 texture)
            {int index=vertices.Count;vertices.Add(new Vector3(p.x,p.y,z));normals.Add(normal);uv.Add(texture);return index;}
            foreach(var triangle in Geometry.Triangles(body.region))
            {
                var p=triangle;
                if(Geometry.SignedArea(p)<0) Array.Reverse(p);
                int front=vertices.Count;
                foreach(var point in p) Vertex(point,-depth,Vector3.back,body.UV(point));
                indices[0].AddRange(new[] {front,front+2,front+1});
                int back=vertices.Count;
                foreach(var point in p) Vertex(point,depth,Vector3.forward,body.UV(point));
                indices[0].AddRange(new[] {back,back+1,back+2});
            }
            foreach(var edge in Geometry.Edges(Geometry.Normalize(body.region)))
            {
                var delta=edge.b-edge.a;var normal=new Vector3(delta.y,-delta.x,0).normalized;int start=vertices.Count;
                Vertex(edge.a,-depth,normal,Vector2.zero);Vertex(edge.b,-depth,normal,new Vector2(edge.Length,0));
                Vertex(edge.b,depth,normal,new Vector2(edge.Length,body.thickness));Vertex(edge.a,depth,normal,new Vector2(0,body.thickness));
                indices[edge.cut?2:1].AddRange(new[] {start,start+1,start+2,start,start+2,start+3});
            }
            var mesh=new Mesh {name="BOM current region extrusion",indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.DontSave};
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.subMeshCount=3;
            for(int i=0;i<3;i++) mesh.SetTriangles(indices[i],i);mesh.RecalculateBounds();return mesh;
        }
        public static Material[] Materials(AppearanceRecord appearance,out Texture2D texture)
        {
            texture=null;var shader=Resources.Load<Shader>("BOMExtrusion");
            if(shader==null) throw new InvalidOperationException("BOM extrusion shader is missing. Restore Resources/BOMExtrusion.shader.");
            if(!string.IsNullOrEmpty(appearance.imagePng))
            {
                texture=new Texture2D(2,2,TextureFormat.RGBA32,false) {name="BOM resolved face image",hideFlags=HideFlags.DontSave};
                if(!ImageConversion.LoadImage(texture,Convert.FromBase64String(appearance.imagePng),false))
                {ObjectLifetime.Release(texture);texture=null;throw new InvalidOperationException("Face image cannot be decoded. Approve a valid PNG image.");}
                texture.wrapMode=appearance.wrapMode;texture.filterMode=appearance.filterMode;
            }
            var result=new Material[3];var colours=new[] {appearance.face,appearance.side,appearance.cut};
            for(int i=0;i<3;i++)
            {
                result[i]=new Material(shader) {name=i==0?"BOM face":i==1?"BOM side":"BOM cut",hideFlags=HideFlags.DontSave};
                result[i].SetColor("_BaseColor",colours[i]);
                if(i==0&&texture!=null) result[i].SetTexture("_BaseMap",texture);
            }
            return result;
        }
    }
}
