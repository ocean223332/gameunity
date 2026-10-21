using System.Linq;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Object = UnityEngine.Object;
namespace TienTuyen.Combat.Tests
{
    public sealed class CommanderAssetTests
    {
        const string Root = "Assets/_TienTuyen/Art/Characters/Commander/";
        [Test] public void CommanderHasWeightedHumanoidBodyAndRetargetableClips()
        {
            var profile = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(Root + "CommanderProfile.asset");
            Assert.That(profile, Is.Not.Null, "Run the commander asset importer.");
            Assert.That(profile.IsValid, Is.True);
            Assert.That(profile.runForward, Is.Not.Null);
            Assert.That(profile.aim, Is.Not.Null);
            var body = profile.model.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(body, Is.Not.Null);
            Assert.That(body.bones.Length, Is.GreaterThanOrEqualTo(19));
            Assert.That(body.sharedMesh.boneWeights.All(w => Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1) < .01f), Is.True);
            Assert.That(body.sharedMesh.bounds.size.x, Is.GreaterThan(body.sharedMesh.bounds.size.z * 1.4f), "Model should face +Z, not sideways.");
            foreach (var clip in new[] { profile.idle, profile.runForward, profile.aim })
            {
                var instance = Object.Instantiate(profile.model);
                try
                {
                    var animator = instance.GetComponent<Animator>();
                    animator.Rebind();
                    var graph = PlayableGraph.Create("CommanderAssetTest");
                    try
                    {
                        var playable = AnimationClipPlayable.Create(graph,clip);
                        var output = AnimationPlayableOutput.Create(graph,"Pose",animator);
                        output.SetSourcePlayable(playable);graph.Play();graph.Evaluate(clip.length*.4f);
                        var baked = new Mesh();
                        instance.GetComponentInChildren<SkinnedMeshRenderer>().BakeMesh(baked);
                        try
                        {
                            Assert.That(baked.vertices.All(v=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)),Is.True);
                            Assert.That(baked.bounds.size.y,Is.InRange(1.2f,2.3f));
                            Assert.That(baked.bounds.size.magnitude,Is.LessThan(3.2f),"Retargeting must not explode the skin.");
                        }
                        finally { Object.DestroyImmediate(baked); }
                    }
                    finally { graph.Destroy(); }
                    var head = animator.GetBoneTransform(HumanBodyBones.Head);
                    Assert.That(head, Is.Not.Null);
                    Assert.That(float.IsNaN(head.position.y), Is.False);
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }
        [Test] public void Rpg18HasBoundedGeometryAndUrpMaterial()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "RPG18.prefab");
            Assert.That(model, Is.Not.Null);
            var mesh = model.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(mesh.triangles.Length / 3, Is.LessThanOrEqualTo(25000));
            var material = model.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(material.mainTexture, Is.Not.Null);
            Assert.That(model.transform.Find("Muzzle"), Is.Not.Null);
            Assert.That(model.transform.Find("Rear"), Is.Not.Null);
        }
    }
}

