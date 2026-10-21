using System;
using System.IO;
using System.Linq;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TienTuyen.Editor
{
    /// <summary>Imports the user's boss assets without changing existing enemy profiles or combat scenes.</summary>
    public static class CommanderAssetImporter
    {
        public const string Root = "Assets/_TienTuyen/Art/Characters/Commander";
        const string ScenePath = "Assets/_TienTuyen/Scenes/CommanderReview.unity";
        [Serializable] sealed class Geometry
        {
            public Vector3[] positions, normals;
            public Vector2[] uv;
            public int[] triangles;
            public BoneData[] bones;
            public WeightData[] weights;
        }
        [Serializable] sealed class BoneData { public string name; public int parent; public Vector3 position; }
        [Serializable] sealed class WeightData { public int[] indices; public float[] weights; }

        [MenuItem("Tien Tuyen/Characters/Import Commander Assets")]
        public static void Import()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Root);
            string generated = Path.GetFullPath(Path.Combine(Application.dataPath, "../../output/commander-assets"));
            Directory.CreateDirectory(Root + "/Textures");
            if (Directory.Exists(Path.Combine(generated,"Textures")))
            {
                foreach (var texture in Directory.GetFiles(Path.Combine(generated,"Textures"),"*.png"))
                    File.Copy(texture,Root+"/Textures/"+Path.GetFileName(texture),true);
                foreach (string name in new[] {"CommanderGeometry.json","RPG18Geometry.json"})
                    File.Copy(Path.Combine(generated,name),Root+"/"+name,true);
            }
            if (!File.Exists(Root+"/CommanderGeometry.json") || !File.Exists(Root+"/RPG18Geometry.json"))
                throw new FileNotFoundException("Generate commander assets using tools/boss_assets/build_assets.py first.");
            AssetDatabase.DeleteAsset(Root+"/Commander_Rig.fbx");
            AssetDatabase.DeleteAsset(Root+"/RPG18.fbx");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // Read normalized mesh data directly: avoid FBX axis and skeleton-conversion differences.
            var geometry = JsonUtility.FromJson<Geometry>(File.ReadAllText(Root + "/CommanderGeometry.json"));
            var body = new GameObject("Commander");
            var bones = new Transform[geometry.bones.Length];
            for (int i=0;i<bones.Length;i++)
            {
                var data=geometry.bones[i]; bones[i]=new GameObject(data.name).transform;
                bones[i].SetParent(data.parent<0 ? body.transform : bones[data.parent],false);
                bones[i].localPosition=data.position;
            }
            var mesh=MeshAsset(geometry,"CommanderBody");
            mesh.bindposes=bones.Select(b=>b.worldToLocalMatrix*body.transform.localToWorldMatrix).ToArray();
            mesh.boneWeights=geometry.weights.Select(w=>new BoneWeight {
                boneIndex0=w.indices[0],boneIndex1=w.indices[1],boneIndex2=w.indices[2],boneIndex3=w.indices[3],
                weight0=w.weights[0],weight1=w.weights[1],weight2=w.weights[2],weight3=w.weights[3]}).ToArray();
            EditorUtility.SetDirty(mesh);
            var renderer=body.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=mesh;renderer.bones=bones;
            renderer.rootBone=bones[0];renderer.sharedMaterial=Material("Commander");renderer.updateWhenOffscreen=true;
            // Avatar reference is a T-pose; skin bind matrices remain those of the source A-pose.
            var original=bones.Select(b=>b.localRotation).ToArray();
            foreach (string side in new[]{"Left","Right"})
            {
                var upper=bones.Single(b=>b.name==side+"UpperArm");
                var lower=bones.Single(b=>b.name==side+"LowerArm");
                upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,side=="Left" ? Vector3.left : Vector3.right)*upper.rotation;
            }
            var all=body.GetComponentsInChildren<Transform>();
            var description=new HumanDescription {
                human=bones.Select(b=>new HumanBone { boneName=b.name,humanName=HumanTrait.BoneName[(int)Enum.Parse(typeof(HumanBodyBones),b.name)],limit=new HumanLimit{useDefaultValues=true}}).ToArray(),
                skeleton=all.Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray(),
                upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=.05f,legStretch=.05f};
            var avatar=AvatarBuilder.BuildHumanAvatar(body,description);avatar.name="CommanderAvatar";
            if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Commander Humanoid avatar is invalid.");
            var avatarPath=Root+"/CommanderAvatar.asset";
            var savedAvatar=AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath);
            if(savedAvatar==null){AssetDatabase.CreateAsset(avatar,avatarPath);savedAvatar=avatar;}
            else {EditorUtility.CopySerialized(avatar,savedAvatar);UnityEngine.Object.DestroyImmediate(avatar);}
            for(int i=0;i<bones.Length;i++) bones[i].localRotation=original[i];
            var animator=body.AddComponent<Animator>();animator.avatar=savedAvatar;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var prefab=PrefabUtility.SaveAsPrefabAsset(body,Root+"/Commander.prefab");UnityEngine.Object.DestroyImmediate(body);
            var profile=AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(Root+"/CommanderProfile.asset");
            if(profile==null){profile=ScriptableObject.CreateInstance<SkinnedCharacterProfile>();AssetDatabase.CreateAsset(profile,Root+"/CommanderProfile.asset");}
            profile.model=prefab;profile.avatar=savedAvatar;
            const string clips="Assets/_TienTuyen/Art/Characters/USRifleman/Animations/";
            profile.idle=Clip(clips+"HeroShared/PAVN_Hero@Idle_Rifle.fbx");profile.aim=Clip(clips+"HeroShared/PAVN_Hero@Aim_Hold.fbx");profile.runForward=Clip(clips+"US_Rifleman@Run_Forward.fbx");
            profile.modelScale=1;profile.runForwardSpeed=4.7f;profile.maxPlaybackRate=1.3f;EditorUtility.SetDirty(profile);
            var weapon=new GameObject("RPG18");
            var weaponGeometry=JsonUtility.FromJson<Geometry>(File.ReadAllText(Root+"/RPG18Geometry.json"));
            weapon.AddComponent<MeshFilter>().sharedMesh=MeshAsset(weaponGeometry,"RPG18Mesh");weapon.AddComponent<MeshRenderer>().sharedMaterial=Material("RPG18");
            Socket(weapon.transform,"Muzzle",new Vector3(0,0,.525f));Socket(weapon.transform,"Rear",new Vector3(0,0,-.525f));
            Socket(weapon.transform,"RightGrip",new Vector3(0,-.16f,.16f));Socket(weapon.transform,"LeftGrip",new Vector3(-.08f,-.15f,.3f));
            PrefabUtility.SaveAsPrefabAsset(weapon,Root+"/RPG18.prefab");UnityEngine.Object.DestroyImmediate(weapon);
            AssetDatabase.SaveAssets();ReviewScene(profile);
            Debug.Log("[CommanderAssets] Imported Humanoid, retargeted clips, RPG18 and review scene.");
        }
        static Mesh MeshAsset(Geometry data,string name)
        {
            var created=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
            created.vertices=data.positions;created.normals=data.normals;created.uv=data.uv;created.triangles=data.triangles;created.RecalculateBounds();
            created.RecalculateTangents();
            string path=Root+"/"+name+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing==null){AssetDatabase.CreateAsset(created,path);return created;}
            EditorUtility.CopySerialized(created,existing);UnityEngine.Object.DestroyImmediate(created);return existing;
        }
        static void Socket(Transform parent, string name, Vector3 position)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position;
        }
        static AnimationClip Clip(string path)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null || !clip.isHumanMotion) throw new InvalidOperationException("Missing Humanoid clip: " + path);
            return clip;
        }
        static Texture2D Texture(string name, string suffix)
        {
            string path = Root + "/Textures/" + name + "_" + suffix + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;
            importer.textureType = suffix == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = suffix == "BaseColor";
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Material Material(string name)
        {
            string path = Root + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetTexture("_BaseMap", Texture(name, "BaseColor"));
            var normal = Texture(name, "Normal");
            if (normal != null) { material.SetTexture("_BumpMap", normal); material.EnableKeyword("_NORMALMAP"); }
            var packed = Texture(name, "MetallicSmoothness");
            if (packed != null) { material.SetTexture("_MetallicGlossMap", packed); material.SetFloat("_Smoothness", 1); material.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            EditorUtility.SetDirty(material);
            return material;
        }
        static void ReviewScene(SkinnedCharacterProfile profile)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "ReviewFloor"; floor.transform.localScale = Vector3.one * 2;
            var floorMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/ReviewFloor.mat");
            if (floorMat == null) { floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit")); floorMat.color = new Color(.25f,.27f,.25f); AssetDatabase.CreateAsset(floorMat, Root + "/ReviewFloor.mat"); }
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            var key = new GameObject("KeyLight").AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.3f; key.transform.rotation = Quaternion.Euler(35,-30,0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.5f,.5f,.5f);
            var camera = new GameObject("ReviewCamera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.orthographic = true; camera.orthographicSize = 1.6f; camera.transform.position = new Vector3(0,1.6f,5); camera.transform.LookAt(new Vector3(0,.95f,0));
            camera.backgroundColor = new Color(.18f,.2f,.22f); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var clips = new[] { profile.idle, profile.runForward, profile.aim };
            var labels = new[] { "Idle", "Run", "Aim" };
            for (int i=0;i<3;i++)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(profile.model);
                model.name = "Commander_" + labels[i]; model.transform.position = new Vector3((i-1)*1.6f,0,0);
                string path = Root + "/Review" + labels[i] + ".controller";
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path) ?? AnimatorController.CreateAnimatorControllerAtPath(path);
                var machine = controller.layers[0].stateMachine;
                foreach (var state in machine.states) machine.RemoveState(state.state);
                var idle = machine.AddState(labels[i]); idle.motion = clips[i]; machine.defaultState = idle;
                model.GetComponent<Animator>().runtimeAnimatorController = controller;
            }
            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/RPG18.prefab"));
            weapon.transform.position = new Vector3(0,.6f,1.1f); weapon.transform.rotation = Quaternion.Euler(0,90,0);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Directory.CreateDirectory("../Builds/CommanderReview");
            // Render each retargeted pose with the same lighting for deformation review.
            var models = scene.GetRootGameObjects().Where(o => o.name.StartsWith("Commander_")).ToArray();
            for (int i=0;i<models.Length;i++)
            {
                var graph = PlayableGraph.Create("CommanderPoseReview");
                try
                {
                    var playable = AnimationClipPlayable.Create(graph, clips[i]);
                    var output = AnimationPlayableOutput.Create(graph,"Pose",models[i].GetComponent<Animator>());
                    output.SetSourcePlayable(playable); graph.Play(); graph.Evaluate(.3f);
                }
                finally { graph.Destroy(); }
            }
            var texture = new RenderTexture(1600,900,24); camera.targetTexture = texture; camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = texture;
            var image = new Texture2D(1600,900,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1600,900),0,0); image.Apply();
            File.WriteAllBytes("../Builds/CommanderReview/poses.png",image.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(image); texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.SaveAssets();
        }
    }
}
