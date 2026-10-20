using System.Collections;
using System.Linq;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace TienTuyen.Combat.Tests
{
 public sealed class CommanderVisualTests
 {
  static void Capture(string name)
  {
   string folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../Builds/CommanderReview"));System.IO.Directory.CreateDirectory(folder);
   var camera=Camera.main;var target=new RenderTexture(1280,720,24);camera.targetTexture=target;
   var canvases=Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c=>c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
   foreach(var canvas in canvases){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+.1f;}
   Canvas.ForceUpdateCanvases();camera.Render();
   var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
   System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,name+".png"),image.EncodeToPNG());
   foreach(var canvas in canvases){canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;}
   RenderTexture.active=previous;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);
  }
  [UnityTest]public IEnumerator SceneContainsBossVisualAndHealthBarAndCameraRestoresAfterIntro()
  {
   yield return SceneManager.LoadSceneAsync("CombatSpike");var game=Object.FindFirstObjectByType<CombatGame>();game.enabled=false;yield return null;
   var presentation=game.GetComponent<CommanderPresentation>();Assert.That(presentation,Is.Not.Null);Assert.That(presentation.Profile.IsValid,Is.True);
   game.BeginRun();game.ExecuteCheat("boss");yield return null;yield return null;
   Assert.That(GameObject.Find("CommanderVisual"),Is.Not.Null);Assert.That(Camera.main.GetComponent<CombatThirdPersonCamera>().enabled,Is.True,"Camera Update must keep handling pause cursor during a cutscene.");
   Assert.That((bool)typeof(CommanderPresentation).GetField("cameraOwned",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(presentation),Is.True,"Cutscene must acquire the dynamically created camera controller.");
   Capture("boss-intro");yield return null;
   game.StepSimulation(6.1f,Vector2.zero);yield return null;yield return null;
   Assert.That(Camera.main.GetComponent<CombatThirdPersonCamera>().enabled,Is.True);
   var health=GameObject.Find("CommanderHealth");Assert.That(health,Is.Not.Null);
   Assert.That(health.GetComponentsInChildren<Image>().Any(i=>i.type==Image.Type.Filled && i.fillAmount>.99f),Is.True);
   typeof(CombatGame).GetField("commanderAttack",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(game,0f);
   game.StepSimulation(.08f,Vector2.zero);yield return null;yield return null;
   var warning=game.transform.Find("EnemyWarning_0");Assert.That(warning.gameObject.activeSelf,Is.True);
   Assert.That(warning.localScale.x,Is.EqualTo(5f).Within(.01f),"Warning must match the 2.5m blast radius after presentation updates.");
   Capture("boss-fight");yield return null;
   game.DamageCommander(100000);yield return null;game.StepSimulation(5.1f,Vector2.zero);yield return null;
   Assert.That(game.Run.LauncherSlot,Is.GreaterThanOrEqualTo(0));game.ReturnToMenu();yield return null;
   Assert.That(GameObject.Find("CommanderVisual"),Is.Null);Assert.That(Camera.main.GetComponent<CombatThirdPersonCamera>().enabled,Is.True);
  }
 }
}
