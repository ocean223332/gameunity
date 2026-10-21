using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace TienTuyen.Combat.Tests
{
 public sealed class CommanderEncounterTests
 {
  CombatGame game;
  object Read(string name){var p=typeof(CombatGame).GetProperty(name);Assert.That(p,Is.Not.Null,name);return p.GetValue(game);}
  [UnitySetUp]public IEnumerator Setup(){yield return SceneManager.LoadSceneAsync("CombatSpike");game=Object.FindFirstObjectByType<CombatGame>();game.enabled=false;yield return null;game.BeginRun();game.SetViewInput(0,Vector3.forward*10,false);}
  [UnityTest]public IEnumerator BossCheatStartsWaveFiveAndCinematicFreezesRunTimer(){game.ExecuteCheat("boss");Assert.That(game.Wave,Is.EqualTo(5));Assert.That((bool)Read("CommanderCinematic"),Is.True);float before=game.RunElapsed;game.StepSimulation(.5f,Vector2.one);Assert.That(game.RunElapsed,Is.EqualTo(before));Assert.That(game.DebugCheatsUsed,Is.True);yield break;}
  [UnityTest]public IEnumerator BossCannotBeSkippedForRewardAndRestartClearsEncounter(){game.ExecuteCheat("boss");game.ExecuteCheat("nextwave");Assert.That(game.Wave,Is.EqualTo(5));game.Restart();Assert.That((bool)Read("CommanderActive"),Is.False);Assert.That(game.Run.LauncherSlot,Is.EqualTo(-1));yield break;}
  [UnityTest]public IEnumerator DefeatAwardsLauncherOnceAfterRetreatAndEnablesUpgrade(){game.ExecuteCheat("boss");game.StepSimulation(10,Vector2.zero);Assert.That((bool)Read("CommanderCinematic"),Is.False);var hit=typeof(CombatGame).GetMethod("DamageCommander");Assert.That(hit,Is.Not.Null);hit.Invoke(game,new object[]{100000f});Assert.That((bool)Read("CommanderCinematic"),Is.True);game.StepSimulation(10,Vector2.zero);Assert.That(game.Run.CommanderRewardClaimed,Is.True);Assert.That(game.Run.LauncherSlot,Is.GreaterThanOrEqualTo(0));Assert.That(game.State,Is.EqualTo(CombatState.Upgrade));long money=game.Run.CurrencyMinor;hit.Invoke(game,new object[]{100000f});game.StepSimulation(10,Vector2.zero);Assert.That(game.Run.CurrencyMinor,Is.EqualTo(money));yield break;}
  [UnityTest] public IEnumerator AutomaticLauncherTravelsTowardEnemyOutsideForwardRay()
  {
   game.ExecuteCheat("boss");game.StepSimulation(6.1f,Vector2.zero);
   game.CommanderActor.position=new Vector3(-6,.8f,0);
   game.Run.TryClaimCommanderReward(0);((int[])typeof(CombatGame).GetField("weaponAmmo",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(game))[game.Run.LauncherSlot]=1;game.SwitchWeapon(game.Run.LauncherSlot);game.StepSimulation(.6f,Vector2.zero);
   typeof(CombatGame).GetField("manualAim",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(game,false);
   typeof(CombatGame).GetMethod("FireLauncher",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(game,null);
   var rockets=(System.Collections.IList)typeof(CombatGame).GetField("rockets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(game);
   Assert.That(rockets.Count,Is.EqualTo(1));var rocket=rockets[0];
   var direction=(Vector3)rocket.GetType().GetField("direction").GetValue(rocket);
   Assert.That(Vector3.Dot(direction,Vector3.left),Is.GreaterThan(.95f));yield break;
  }  [UnityTest]public IEnumerator RocketWarningLocksItsTargetAndCrouchingDoesNotAvoidBlast()
  {
   game.ExecuteCheat("boss");game.StepSimulation(6.1f,Vector2.zero);game.CommanderActor.position=new Vector3(6,.8f,0);
   typeof(CombatGame).GetField("sniperTutorialSeen",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(game,true);
   typeof(CombatGame).GetField("commanderAttack",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(game,0f);
   game.SetCrouchInput(true);float health=game.Health;game.StepSimulation(.04f,Vector2.zero);
   Assert.That(game.CommanderAiming,Is.True);var target=game.CommanderTarget;
   game.StepSimulation(.8f,Vector2.right);Assert.That(game.CommanderTarget,Is.EqualTo(target));
   var player=(Transform)typeof(CombatGame).GetField("player",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(game);player.position=new Vector3(target.x,.8f,target.z);
   game.StepSimulation(2.4f,Vector2.zero);Assert.That(game.Crouch,Is.GreaterThan(.99f));Assert.That(game.Health,Is.LessThan(health));yield break;
  } }
}
