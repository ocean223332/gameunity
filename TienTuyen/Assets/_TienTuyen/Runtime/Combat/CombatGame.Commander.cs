using System;
using System.Collections.Generic;
using TienTuyen.Content;
using UnityEngine;
namespace TienTuyen.Combat
{
    // Encounter logic lives with combat so presentation cannot decide damage or rewards.
    public sealed partial class CombatGame
    {
        public const float CommanderMaxHealth = 1000f, CommanderTelegraphSeconds = 1.8f;
        private Enemy commander;
        private int commanderPhase; // 0 absent, 1 intro, 2 fight, 3 retreat
        private float commanderClock, commanderAttack, commanderMove, skipCommanderHold;
        private Vector3 commanderTarget, commanderWalk;
        private bool commanderAiming;
        public bool CommanderActive => commander != null && commanderPhase != 0;
        public bool CommanderCinematic => CommanderActive && (commanderPhase == 1 || commanderPhase == 3);
        public bool CommanderRetreating => commanderPhase == 3;
        public bool CommanderAiming => commanderPhase == 2 && commanderAiming;
        public Transform CommanderActor => commander?.body.transform;
        public float CommanderHealth => commanderPhase == 3 ? 0 : Mathf.Max(0,commander?.hp ?? 0);
        public float CommanderCinematicProgress => Mathf.Clamp01(commanderClock/(CommanderRetreating ? 5f : 6f));
        public Vector3 CommanderTarget => commanderTarget;
        public event Action<Vector3,float,bool> RocketExploded;
        public event Action<bool> LauncherFired;
        public bool IsCommanderActor(Transform actor) => CommanderActive && commander.body.transform == actor;
        public bool IsCommanderWarning(Transform marker) => CommanderActive && commander.warning.transform==marker;
        private sealed class Rocket
        {
            public GameObject body; public Vector3 direction; public float left,damage,radius; public bool hostile;
        }
        private readonly List<Rocket> rockets = new List<Rocket>();
        public bool LauncherEquipped => Run != null && ActiveWeapon?.Id == ContentIds.Rpg18 && !KnifeOut && !KnifeSwinging;

        private void ResetCommander()
        {
            commanderPhase=0;commander=null;commanderAiming=false;skipCommanderHold=0;
            foreach(var rocket in rockets) if(rocket.body!=null) Destroy(rocket.body);
            rockets.Clear();
        }
        private void StartCommander()
        {
            commander=enemies[0];commander.definition=new EnemyDefinition("enemy.commander","ĐẠI ÚY · CHỈ HUY",EnemyRole.Shooter,CommanderMaxHealth,2f,35f,4f,22f,CommanderTelegraphSeconds,0,0,0);
            commander.active=true;commander.pending=false;commander.hp=CommanderMaxHealth;commander.shooter=false;
            Vector3 point=new Vector3(11,.8f,6);
            for(int i=0;i<60;i++)
            {
                var candidate=new Vector3((float)(rng.NextDouble()*26-13),.8f,(float)(rng.NextDouble()*14-7));
                if(CombatRules.IsSpawnSafe(candidate,player.position) && !IsInsideCover(new Vector2(candidate.x,candidate.z),.6f)){point=candidate;break;}
            }
            commander.body.transform.position=point;commander.body.transform.localScale=new Vector3(.7f,.85f,.7f);commander.body.SetActive(true);
            commander.warning.SetActive(false);commander.laser.SetActive(false);AliveEnemies++;
            commanderPhase=1;commanderClock=0;commanderAiming=false;commanderAttack=1.8f;commanderMove=0;
        }
        private void AdvanceCommanderCinematic(float seconds)
        {
            if(!CommanderCinematic || State!=CombatState.Playing) return;
            commanderClock+=Mathf.Max(0,seconds);
            if(commanderPhase==3)
            {
                MoveWithCover(commander.body.transform,new Vector3(1,0,.25f)*Mathf.Min(seconds,.1f)*1.1f,.5f);
                if(commanderClock<5f) return;
                // The reward is owned by the run and idempotent, not by a cutscene callback.
                if(Run.TryClaimCommanderReward(ActiveWeaponSlot))
                {
                    heldWeapon=Run.Weapons[Run.LauncherSlot];KeepHeldWeaponInHand();
                    weaponAmmo[ActiveWeaponSlot]=1;weaponReload[ActiveWeaponSlot]=weaponFireTimer[ActiveWeaponSlot]=0;
                }
                commanderPhase=0;commander.body.SetActive(false);commander=null;
                FinishWave();
            }
            else if(commanderClock>=6f) {commanderPhase=2;commanderClock=0;invulnerability=1f;}
        }
        public void DamageCommander(float damage)
        {
            if(State!=CombatState.Playing || commanderPhase!=2 || commander==null || !commander.active) return;
            ApplyShotDamage(commander,damage,false);
        }
        private void CommanderDefeated()
        {
            commander.active=false;commanderAiming=false;commander.warning.SetActive(false);commanderPhase=3;commanderClock=0;AliveEnemies=Mathf.Max(0,AliveEnemies-1);Kills++;
            // Nothing can hit the hero or survive into the retreat cutscene.
            foreach(var enemy in enemies) if(!ReferenceEquals(enemy,commander)) {enemy.active=enemy.pending=false;enemy.body.SetActive(false);enemy.warning.SetActive(false);enemy.laser.SetActive(false);}
            foreach(var bullet in bullets) Deactivate(bullet);
            foreach(var rocket in rockets) if(rocket.body!=null) Destroy(rocket.body);
            rockets.Clear();AliveEnemies=0;
        }
        private void UpdateCommander(float dt)
        {
            if(commanderPhase!=2 || commander==null) return;
            commanderAttack-=dt;
            if(commanderAiming)
            {
                commander.warning.transform.position=new Vector3(commanderTarget.x,.04f,commanderTarget.z);
                commander.warning.transform.localScale=new Vector3(5f,.025f,5f);commander.warning.transform.rotation=Quaternion.identity;
                if(commanderAttack<=0)
                {
                    commanderAiming=false;commander.warning.SetActive(false);
                    LaunchRocket(commander.body.transform.position+Vector3.up*.65f,commanderTarget,35,2.5f,true);
                    commanderAttack=commander.hp<CommanderMaxHealth*.5f ? 2.2f : 3f;
                    Vector3 side=Vector3.Cross((player.position-commander.body.transform.position).normalized,Vector3.up);
                    commanderWalk=side*(rng.Next(2)==0 ? 1 : -1);commanderMove=1.4f;
                }
            }
            else if(commanderAttack<=0)
            {
                commanderTarget=new Vector3(player.position.x,.08f,player.position.z);commanderAiming=true;commanderAttack=CommanderTelegraphSeconds;commander.warning.SetActive(true);
            }
            else if(commanderMove>0)
            {
                commanderMove-=dt;MoveWithCover(commander.body.transform,commanderWalk*2f*dt,.5f);
                var p=commander.body.transform.position;commander.body.transform.position=new Vector3(Mathf.Clamp(p.x,-HalfWidth+.6f,HalfWidth-.6f),p.y,Mathf.Clamp(p.z,-HalfHeight+.6f,HalfHeight-.6f));
            }
        }
        private void LaunchRocket(Vector3 from,Vector3 destination,float damage,float radius,bool hostile)
        {
            Vector3 delta=destination-from;if(delta.sqrMagnitude<.01f) delta=aimDirection;
            var body=GameObject.CreatePrimitive(PrimitiveType.Capsule);body.name=hostile ? "CommanderRocket" : "PlayerRocket";body.transform.SetParent(transform,false);
            var collider=body.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            body.transform.position=from;body.transform.rotation=Quaternion.FromToRotation(Vector3.up,delta.normalized);body.transform.localScale=new Vector3(.09f,.2f,.09f);
            if(ProjectileMaterial!=null) body.GetComponent<Renderer>().sharedMaterial=ProjectileMaterial;
            rockets.Add(new Rocket{body=body,direction=delta.normalized,left=Mathf.Min(40,delta.magnitude),damage=damage,radius=radius,hostile=hostile});
            LauncherFired?.Invoke(hostile);
        }
        private void UpdateRockets(float dt)
        {
            for(int i=rockets.Count-1;i>=0;i--)
            {
                var rocket=rockets[i];Vector3 start=rocket.body.transform.position;
                float travel=Mathf.Min(rocket.left,18*dt);bool blocked=RayHitsCover(start,rocket.direction,travel,out float wall);
                if(blocked) travel=Mathf.Max(0,wall-.03f);
                rocket.body.transform.position+=rocket.direction*travel;rocket.left-=travel;
                if(!blocked && rocket.left>.001f) continue;
                rockets.RemoveAt(i);Vector3 hit=rocket.body.transform.position;Destroy(rocket.body);
                RocketExploded?.Invoke(hit,rocket.radius,rocket.hostile);
                if(rocket.hostile)
                {
                    if(PlanarDistanceSquared(player.position,hit)<=rocket.radius*rocket.radius && HasLineOfSight(hit+Vector3.up*.25f,player.position)) DamagePlayer(rocket.damage,hit);
                }
                else
                {
                    foreach(var enemy in enemies)
                    {
                        if(!enemy.active || PlanarDistanceSquared(enemy.body.transform.position,hit)>rocket.radius*rocket.radius) continue;
                        if(HasLineOfSight(hit+Vector3.up*.25f,enemy.body.transform.position)) ApplyShotDamage(enemy,rocket.damage,false);
                        if(CommanderCinematic) break;
                    }
                }
                if(State!=CombatState.Playing || CommanderCinematic) return;
            }
        }
        private void FireLauncher()
        {
            if(!ActiveWeaponReady() || manualAim && !triggerHeld) return;
            int target=manualAim ? FindAimTarget(36) : FindTarget(24);
            if(!manualAim && target<0) return;
            Vector3 from=player.position+Vector3.up*.65f;
            Vector3 origin=hasViewRay ? viewRayOrigin : from;
            Vector3 direction=hasViewRay ? viewRayDirection : aimDirection;
            float distance=30;
            if(target>=0) distance=Vector3.Distance(origin,AimTargetPoint(target,origin,direction));
            else
            {
                if(RayHitsCover(origin,direction,30,out float wall)) distance=Mathf.Min(distance,wall);
                if(RayHitsGround(origin,direction,30,out float ground)) distance=Mathf.Min(distance,ground);
            }
            Vector3 to=target>=0 ? (manualAim ? AimTargetPoint(target,origin,direction) : enemies[target].body.transform.position+Vector3.up*.08f) : origin+direction*distance;
            int slot=ActiveWeaponSlot;weaponAmmo[slot]--;weaponFireTimer[slot]=ActiveWeapon.ShotCooldownSeconds;
            LaunchRocket(from,to,ActiveWeapon.DamagePerHit*Run.RocketDamageFactor*Stats.DamageFactor,Run.RocketRadius,false);
            WeaponFired?.Invoke(slot,from,to);if(weaponAmmo[slot]<=0) StartReload(slot);
        }
    }
}
