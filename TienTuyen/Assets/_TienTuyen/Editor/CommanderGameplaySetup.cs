using System;
using TienTuyen.Combat;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace TienTuyen.Editor
{
 public static class CommanderGameplaySetup
 {
  [MenuItem("Tien Tuyen/Combat/Install Commander Encounter")]
  public static void Install()
  {
   if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
   var scene=EditorSceneManager.OpenScene(CombatSceneSetup.ScenePath,OpenSceneMode.Single);
   var game=UnityEngine.Object.FindFirstObjectByType<CombatGame>();
   var presentation=game.GetComponent<CommanderPresentation>() ?? game.gameObject.AddComponent<CommanderPresentation>();
   presentation.Profile=AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(CommanderAssetImporter.Root+"/CommanderProfile.asset");
   presentation.Launcher=AssetDatabase.LoadAssetAtPath<GameObject>(CommanderAssetImporter.Root+"/RPG18.prefab");
   if(presentation.Profile==null || presentation.Launcher==null)throw new InvalidOperationException("Import commander assets first.");
   EditorUtility.SetDirty(presentation);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
   Debug.Log("[CommanderSetup] Wave-5 gameplay installed. F9: start test encounter.");
  }
 }
}
