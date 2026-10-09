// Unity side. Put this file in any folder named "Editor" under Assets, then run
// Tools > Import sbox Level... and pick the level.json written by the s&box exporter.
// Everything it generates goes to Assets/SboxImport/<level name>/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class SboxLevelImporter
{
    const float Scale = 0.0254f; // s&box units are inches, Unity's are metres

    // Mirrors level.json. Field names must match the exporter's keys exactly.
    [Serializable] public class Level { public string name; public Mat[] materials; public MeshObject[] meshes; public Col[] colliders; }
    [Serializable] public class Xform { public float[] position, rotation, scale; }
    [Serializable] public class Mat { public string name, color, normal, rma; public float[] tint; public float metalness; public bool metalTexture; public int mode; }
    [Serializable] public class Sub { public int material; public float[] positions, normals, uvs; public int[] indices; }
    [Serializable] public class MeshObject { public string name; public Xform transform; public int collision; public bool trigger, hidden; public Sub[] submeshes; }
    [Serializable] public class Col { public string name, type; public Xform transform; public bool trigger; public float[] a, b; public float radius; }

    // s&box: x forward, y left, z up, right-handed. Unity: x right, y up, z forward, left-handed.
    static Vector3 Dir(float[] v, int i = 0) => new Vector3(-v[i + 1], v[i + 2], v[i]);
    static Vector3 Pos(float[] v, int i = 0) => Dir(v, i) * Scale;
    static Vector3 Size(float[] v) => new Vector3(v[1], v[2], v[0]);
    static Quaternion Rot(float[] q) => new Quaternion(q[1], -q[2], -q[0], q[3]);

    [MenuItem("Tools/Import sbox Level...")]
    static void Import()
    {
        var jsonPath = EditorUtility.OpenFilePanel("Pick the level.json exported from s&box", "", "json");
        if (string.IsNullOrEmpty(jsonPath)) return;

        var src = Path.GetDirectoryName(jsonPath);
        var level = JsonUtility.FromJson<Level>(File.ReadAllText(jsonPath));
        var dst = "Assets/SboxImport/" + level.name;
        Directory.CreateDirectory(dst);

        foreach (var m in level.materials)
        {
            if (!string.IsNullOrEmpty(m.color)) File.Copy(Path.Combine(src, m.color), $"{dst}/{m.color}", true);
            if (!string.IsNullOrEmpty(m.normal)) File.Copy(Path.Combine(src, m.normal), $"{dst}/{m.normal}", true);
            if (!string.IsNullOrEmpty(m.rma)) WriteMask(Path.Combine(src, m.rma), $"{dst}/{m.name}_mask.png", m);
        }
        AssetDatabase.Refresh();

        var materials = level.materials.Select(m => MakeMaterial(m, dst)).ToArray();

        var root = new GameObject(level.name);
        var meshAsset = $"{dst}/{level.name}_meshes.asset";
        AssetDatabase.DeleteAsset(meshAsset);

        for (var i = 0; i < level.meshes.Length; i++)
        {
            var o = level.meshes[i];
            var mesh = BuildMesh(o);
            if (i == 0) AssetDatabase.CreateAsset(mesh, meshAsset);
            else AssetDatabase.AddObjectToAsset(mesh, meshAsset);

            var go = Child(root, o.name, o.transform);
            if (!o.hidden) // "Hide In Game" meshes in s&box are collision only
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = o.submeshes.Select(s => materials[s.material]).ToArray();
            }
            if (o.collision != 0)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.convex = o.collision == 2 || o.trigger; // 2 = hull; Unity triggers must be convex
                mc.isTrigger = o.trigger;
                mc.sharedMesh = mesh;
            }
        }

        foreach (var c in level.colliders)
        {
            var go = Child(root, c.name, c.transform);
            Collider collider;
            if (c.type == "box")
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = Pos(c.a);
                box.size = Size(c.b) * Scale;
                collider = box;
            }
            else if (c.type == "sphere")
            {
                var sphere = go.AddComponent<SphereCollider>();
                sphere.center = Pos(c.a);
                sphere.radius = c.radius * Scale;
                collider = sphere;
            }
            else
            {
                var start = Pos(c.a);
                var axis = Pos(c.b) - start;
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.center = start + axis / 2;
                capsule.radius = c.radius * Scale;
                capsule.height = axis.magnitude + capsule.radius * 2;
                // ponytail: Unity capsules only run along a local axis, so this snaps to the nearest one.
                // Parent the collider to a rotated child object if a level has diagonal capsules.
                var abs = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
                capsule.direction = abs.x > abs.y && abs.x > abs.z ? 0 : abs.y > abs.z ? 1 : 2;
                collider = capsule;
            }
            collider.isTrigger = c.trigger;
        }

        // Saved as a prefab rather than left in the scene, so re-importing updates instances instead of duplicating them.
        AssetDatabase.SaveAssets();
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{dst}/{level.name}.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"Imported {level.meshes.Length} meshes, {level.colliders.Length} colliders, {level.materials.Length} materials. Drag {dst}/{level.name}.prefab into your scene.");
    }

    static GameObject Child(GameObject root, string name, Xform t)
    {
        var go = new GameObject(name) { isStatic = true };
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = Pos(t.position);
        go.transform.localRotation = Rot(t.rotation);
        go.transform.localScale = Size(t.scale);
        return go;
    }

    static Mesh BuildMesh(MeshObject o)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int[]>();

        foreach (var s in o.submeshes)
        {
            var first = vertices.Count;
            for (var i = 0; i < s.positions.Length / 3; i++)
            {
                vertices.Add(Pos(s.positions, i * 3));
                normals.Add(Dir(s.normals, i * 3));
                uvs.Add(new Vector2(s.uvs[i * 2], 1 - s.uvs[i * 2 + 1])); // s&box v runs top to bottom
            }

            // Going right-handed to left-handed mirrors the mesh, so swap two corners to keep faces pointing out.
            var tris = new int[s.indices.Length];
            for (var i = 0; i < tris.Length; i += 3)
            {
                tris[i] = first + s.indices[i];
                tris[i + 1] = first + s.indices[i + 2];
                tris[i + 2] = first + s.indices[i + 1];
            }
            triangles.Add(tris);
        }

        var mesh = new Mesh { name = o.name };
        if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = triangles.Count;
        for (var i = 0; i < triangles.Count; i++) mesh.SetTriangles(triangles[i], i);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    // s&box packs r = roughness, g = metalness, b = occlusion. Repack to r = metallic, g = occlusion,
    // a = smoothness, which the Built-in, URP and HDRP lit shaders can all read from one texture.
    static void WriteMask(string rmaPath, string maskPath, Mat m)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        texture.LoadImage(File.ReadAllBytes(rmaPath));
        var pixels = texture.GetPixels32();
        var metalness = (byte)(Mathf.Clamp01(m.metalness) * 255);
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            pixels[i] = new Color32(m.metalTexture ? p.g : metalness, p.b, 0, (byte)(255 - p.r));
        }
        texture.SetPixels32(pixels);
        File.WriteAllBytes(maskPath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
    }

    static Texture2D LoadTexture(string path, TextureImporterType type, bool sRGB)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return null;
        if (importer.textureType != type || importer.sRGBTexture != sRGB)
        {
            importer.textureType = type;
            importer.sRGBTexture = sRGB;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material MakeMaterial(Mat m, string dst)
    {
        var path = $"{dst}/{m.name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material; // keeps your edits on re-import; delete the .mat to regenerate it

        // Whatever lit shader the project's render pipeline uses: Standard, URP/Lit or HDRP/Lit.
        var pipeline = GraphicsSettings.currentRenderPipeline;
        material = new Material(pipeline != null ? pipeline.defaultMaterial.shader : Shader.Find("Standard"));

        // Property names differ per pipeline. Setting one a shader doesn't have is harmless, so set them all.
        material.color = new Color(m.tint[0], m.tint[1], m.tint[2], 1);
        if (!string.IsNullOrEmpty(m.color))
            material.mainTexture = LoadTexture($"{dst}/{m.color}", TextureImporterType.Default, true);

        if (!string.IsNullOrEmpty(m.normal))
        {
            var normal = LoadTexture($"{dst}/{m.normal}", TextureImporterType.NormalMap, false);
            material.SetTexture("_BumpMap", normal);   // Built-in, URP
            material.SetTexture("_NormalMap", normal); // HDRP
            material.EnableKeyword("_NORMALMAP");
        }

        if (!string.IsNullOrEmpty(m.rma))
        {
            var mask = LoadTexture($"{dst}/{m.name}_mask.png", TextureImporterType.Default, false);
            material.SetTexture("_MetallicGlossMap", mask); // Built-in, URP
            material.SetTexture("_OcclusionMap", mask);     // Built-in, URP
            material.SetTexture("_MaskMap", mask);          // HDRP
            material.EnableKeyword("_METALLICGLOSSMAP");     // Built-in
            material.EnableKeyword("_METALLICSPECGLOSSMAP"); // URP
            material.EnableKeyword("_OCCLUSIONMAP");         // URP
            material.EnableKeyword("_MASKMAP");              // HDRP
            material.SetFloat("_GlossMapScale", 1); // Built-in: use the map's smoothness as is
            material.SetFloat("_Smoothness", 1);    // URP: same
        }
        else
        {
            material.SetFloat("_Metallic", m.metalness);
        }

        if (m.mode == 1) // alpha tested (fences, foliage)
        {
            material.SetFloat("_Mode", 1);      // Built-in
            material.SetFloat("_AlphaClip", 1); // URP
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else if (m.mode == 2) // translucent (glass)
        {
            material.SetFloat("_Mode", 2);    // Built-in
            material.SetFloat("_Surface", 1); // URP
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_ALPHABLEND_ON");            // Built-in
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); // URP
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
