using System.Collections.Generic;
using TMPro;
using TienTuyen.Combat;
using TienTuyen.Content;
using TienTuyen.Presentation.Interface;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;
namespace TienTuyen.Presentation
{
    [DefaultExecutionOrder(250)]
    [RequireComponent(typeof(CombatGame))]
    public sealed class CommanderPresentation : MonoBehaviour
    {
        public const float HurtVoiceCooldownSeconds = 10f;
        public const float HitPlayerVoiceCooldownSeconds = 8f;
        private const float LockVoiceCooldownSeconds = 12f;
        private static readonly (string clip, string subtitle)[] LockLines =
        {
            ("Lock_1", "“Target... locked.”\nĐã khóa mục tiêu."),
            ("Lock_2", "“There is nowhere to hide!”\nKhông còn chỗ nào để trốn!")
        };
        private static readonly (string clip, string subtitle)[] HitPlayerLines =
        {
            ("HitPlayer_1", "“Direct hit!”\nTrúng đích!"),
            ("HitPlayer_2", "“Stay down, soldier.”\nNằm yên đó, lính."),
            ("HitPlayer_3", "“Is that all you've got?”\nChỉ có thế thôi sao?")
        };
        private static readonly (string clip, string subtitle)[] HurtLines =
        {
            ("Hurt_1", "“You'll pay for that!”\nMày sẽ phải trả giá!"),
            ("Hurt_2", "“Is that the best you can do?”\nMày chỉ làm được đến thế thôi à?"),
            ("Hurt_3", "“Keep pushing! Don't let up!”\nTiến lên! Không được chùn bước!")
        };
        private const string EnragedSubtitle = "“Get me support... now!”\nCần chi viện... ngay!";
        private const string VictorySubtitle = "“Road's clear. Move out.”\nĐường đã thông. Tiến quân.";

        public SkinnedCharacterProfile Profile;
        public GameObject Launcher;
        private CombatGame game;
        private Transform player,bossRoot,bossGun,playerGun;
        private Animator bossAnimator,heroAnimator;
        private Camera cameraView;
        private CombatThirdPersonCamera view;
        private bool cameraOwned,wasCinematic,wasRetreat,wasAiming;
        private bool enragedSpoken,playVictoryOnDefeat;
        private float hurtVoiceCooldown,lockVoiceCooldown,hitPlayerVoiceCooldown,combatSubtitleTimer;
        private string combatSubtitle="";
        private int currentVoicePriority,lockCursor,hitPlayerCursor,hurtCursor,lastHostileRocketFrame=-1;
        private readonly Dictionary<string,AudioClip> voiceClips=new Dictionary<string,AudioClip>();
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private Vector3 lastBoss;
        private GameObject canvasObject,healthRoot,topLetter,bottomLetter;
        private Image health;
        private TMP_Text title,subtitle,notice;
        private AudioSource voice;
        private Material bossWarningMaterial;
        private RocketEffects rocketEffects;
        private void Start()
        {
            game=GetComponent<CombatGame>();player=transform.Find("Player");cameraView=Camera.main;
            if(cameraView!=null)view=cameraView.GetComponent<CombatThirdPersonCamera>();
            voice=gameObject.AddComponent<AudioSource>();voice.spatialBlend=0;voice.volume=.95f;
            PreloadVoiceClips();
            game.RocketExploded+=Explosion;game.TransientsCleared+=Clear;
            rocketEffects=GetComponent<RocketEffects>() ?? gameObject.AddComponent<RocketEffects>();
            game.EnemyDamaged+=OnEnemyDamaged;game.PlayerDamaged+=OnPlayerDamaged;
            BuildHud();
        }
        private void PreloadVoiceClips()
        {
            string[] ids={"Intro","Retreat","Lock_1","Lock_2","HitPlayer_1","HitPlayer_2","HitPlayer_3","Hurt_1","Hurt_2","Hurt_3","Enraged","Victory"};
            foreach(string id in ids)
            {
                var clip=Resources.Load<AudioClip>("CommanderVoice/"+id);
                if(clip!=null)voiceClips[id]=clip;
            }
        }
        private float PlayBossVoice(string clipName,string lineSubtitle,int priority)
        {
            if(voice==null)return 0f;
            if(!voiceClips.TryGetValue(clipName,out var clip) || clip==null)
            {
                clip=Resources.Load<AudioClip>("CommanderVoice/"+clipName);
                if(clip!=null)voiceClips[clipName]=clip;
            }
            if(clip==null)return 0f;
            voice.Stop();
            voice.clip=clip;
            voice.Play();
            currentVoicePriority=priority;
            combatSubtitle=lineSubtitle ?? "";
            combatSubtitleTimer=clip.length+.25f;
            return clip.length;
        }
        private void ResetEncounterVoiceState()
        {
            enragedSpoken=playVictoryOnDefeat=false;
            hurtVoiceCooldown=HurtVoiceCooldownSeconds;
            lockVoiceCooldown=6f;
            hitPlayerVoiceCooldown=combatSubtitleTimer=0f;
            combatSubtitle="";
            currentVoicePriority=0;
            wasAiming=false;
        }
        private void BuildBoss()
        {
            if(Profile==null || !Profile.IsValid || Launcher==null) return;
            bossRoot=new GameObject("CommanderVisual").transform;bossRoot.SetParent(transform,false);bossRoot.localScale=Vector3.one*1.3f;
            var model=Instantiate(Profile.model,bossRoot,false);bossAnimator=model.GetComponent<Animator>();
            graph=PlayableGraph.Create("CommanderLocomotion");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer=AnimationMixerPlayable.Create(graph,2);
            graph.Connect(AnimationClipPlayable.Create(graph,Profile.idle),0,mixer,0);
            graph.Connect(AnimationClipPlayable.Create(graph,Profile.runForward),0,mixer,1);
            var output=AnimationPlayableOutput.Create(graph,"Commander",bossAnimator);output.SetSourcePlayable(mixer);graph.Play();
            bossGun=Instantiate(Launcher,bossRoot,false).transform;bossGun.name="CommanderRPG18";
            lastBoss=game.CommanderActor.position;
        }
        private void LateUpdate()
        {
            if(game==null || player==null)return;
            if(cameraView==null)cameraView=Camera.main;
            if(view==null && cameraView!=null)view=cameraView.GetComponent<CombatThirdPersonCamera>();
            float dt=game.State==CombatState.Playing ? Time.deltaTime : 0;
            bool cinematic=game.CommanderCinematic;
            if(game.CommanderActive && bossRoot==null)BuildBoss();
            if(bossRoot!=null)
            {
                bossRoot.gameObject.SetActive(game.CommanderActive && game.State!=CombatState.Menu && game.State!=CombatState.Defeat);
                if(bossRoot.gameObject.activeSelf)
                {
                    Vector3 actor=game.CommanderActor.position;
                    float speed=dt>0 ? (actor-lastBoss).magnitude/Mathf.Max(.001f,dt) : 0;lastBoss=actor;
                    bossRoot.position=actor-Vector3.up*.8f;
                    Vector3 direction=game.CommanderRetreating ? new Vector3(1,0,.25f) : player.position-actor;direction.y=0;
                    if(direction.sqrMagnitude>.001f)bossRoot.rotation=Quaternion.LookRotation(direction);
                    mixer.SetInputWeight(0,speed>.2f ? 0 : 1);mixer.SetInputWeight(1,speed>.2f ? 1 : 0);graph.Evaluate(dt);
                    Vector3 aim=game.CommanderAiming ? game.CommanderTarget-(actor+Vector3.up*.65f) : bossRoot.forward;
                    FitLauncher(bossAnimator,bossGun,aim);
                }
            }
            if(game.CommanderAiming)
            {
                if(bossWarningMaterial==null)
                {
                    bossWarningMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));bossWarningMaterial.SetColor("_BaseColor",new Color(1,.04f,.015f,.48f));
                    bossWarningMaterial.SetFloat("_Surface",1);bossWarningMaterial.SetFloat("_ZWrite",0);bossWarningMaterial.SetFloat("_SrcBlend",5);bossWarningMaterial.SetFloat("_DstBlend",10);
                    bossWarningMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");bossWarningMaterial.renderQueue=3000;
                }
                var marker=transform.Find("EnemyWarning_0");if(marker!=null)marker.GetComponent<Renderer>().sharedMaterial=bossWarningMaterial;
            }
            if(game.State==CombatState.Paused)voice.Pause();else voice.UnPause();
            if(game.State!=CombatState.Paused)
            {
                hurtVoiceCooldown=Mathf.Max(0f,hurtVoiceCooldown-dt);
                lockVoiceCooldown=Mathf.Max(0f,lockVoiceCooldown-dt);
                hitPlayerVoiceCooldown=Mathf.Max(0f,hitPlayerVoiceCooldown-dt);
                if(combatSubtitleTimer>0f)
                {
                    float subStep=game.State==CombatState.Defeat ? Time.unscaledDeltaTime : dt;
                    combatSubtitleTimer=Mathf.Max(0f,combatSubtitleTimer-subStep);
                }
                if(!voice.isPlaying)currentVoicePriority=0;
            }
            if(cinematic && (!wasCinematic || wasRetreat!=game.CommanderRetreating))
            {
                if(!game.CommanderRetreating)ResetEncounterVoiceState();
                PlayBossVoice(game.CommanderRetreating ? "Retreat" : "Intro","",4);
                if(game.CommanderRetreating) Smoke(game.CommanderActor.position);
            }
            if(!cinematic && wasCinematic)
            {
                voice.Stop();
                combatSubtitleTimer=0f;
                currentVoicePriority=0;
                hurtVoiceCooldown=HurtVoiceCooldownSeconds;
            }
            bool aiming=game.CommanderAiming;
            if(aiming && !wasAiming && !cinematic && game.State==CombatState.Playing)
            {
                if(lockVoiceCooldown<=0f && (!voice.isPlaying || currentVoicePriority<1))
                {
                    var line=LockLines[lockCursor++%LockLines.Length];
                    float d=PlayBossVoice(line.clip,line.subtitle,1);
                    lockVoiceCooldown=LockVoiceCooldownSeconds+d;
                }
            }
            wasAiming=aiming;
            wasCinematic=cinematic;wasRetreat=game.CommanderRetreating;
            UpdateHeroLauncher();UpdateCamera(cinematic,dt);UpdateHud(cinematic);
        }
        private void OnEnemyDamaged(Transform actor,float damage,bool critical,bool killed)
        {
            if(game==null || !game.CommanderActive || game.CommanderCinematic || killed || !game.IsCommanderActor(actor))return;
            if(hurtVoiceCooldown>0f)return;
            if(voice!=null && voice.isPlaying)return;
            float hp=game.CommanderHealth;
            if(!enragedSpoken && hp>0f && hp<=CombatGame.CommanderMaxHealth*.5f)
            {
                enragedSpoken=true;
                float d=PlayBossVoice("Enraged",EnragedSubtitle,3);
                hurtVoiceCooldown=HurtVoiceCooldownSeconds+d;
                return;
            }
            var line=HurtLines[hurtCursor++%HurtLines.Length];
            float hurtDuration=PlayBossVoice(line.clip,line.subtitle,1);
            hurtVoiceCooldown=HurtVoiceCooldownSeconds+hurtDuration;
        }
        private void OnPlayerDamaged(Vector3 source,float damage)
        {
            if(game==null || !game.CommanderActive || game.CommanderCinematic)return;
            if(game.Health<=0f)
            {
                playVictoryOnDefeat=true;
                return;
            }
            if(lastHostileRocketFrame==Time.frameCount && hitPlayerVoiceCooldown<=0f && (!voice.isPlaying || currentVoicePriority<=1))
            {
                var line=HitPlayerLines[hitPlayerCursor++%HitPlayerLines.Length];
                float d=PlayBossVoice(line.clip,line.subtitle,2);
                hitPlayerVoiceCooldown=HitPlayerVoiceCooldownSeconds+d;
            }
        }
        private void UpdateHeroLauncher()
        {
            if(Launcher==null)return;
            if(playerGun==null){playerGun=Instantiate(Launcher,transform,false).transform;playerGun.name="PlayerRPG18";}
            bool firstPerson=view!=null && view.FirstPersonWeight>.5f && cameraView!=null;
            bool active=game.LauncherEquipped && game.State!=CombatState.Menu && game.State!=CombatState.Defeat && !game.CommanderCinematic && !firstPerson;
            playerGun.gameObject.SetActive(active);if(!active)return;
            if(heroAnimator==null)heroAnimator=player.GetComponentInChildren<Animator>();
            if(heroAnimator!=null)FitLauncher(heroAnimator,playerGun,game.PlayerAimDirection);
        }
        public static void FitLauncher(Animator animator,Transform launcher,Vector3 direction)
        {
            if(animator==null || !animator.isHuman || launcher==null)return;
            var shoulder=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if(shoulder==null)return;
            if(direction.sqrMagnitude<.001f)direction=animator.transform.forward;
            launcher.rotation=Quaternion.LookRotation(direction.normalized,Vector3.up);
            launcher.position=shoulder.position+launcher.right*.02f+launcher.forward*.06f;
            SolveArm(animator,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,launcher.Find("RightGrip"));
            SolveArm(animator,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,launcher.Find("LeftGrip"));
        }
        private static void SolveArm(Animator animator,HumanBodyBones a,HumanBodyBones b,HumanBodyBones c,Transform grip)
        {
            var upper=animator.GetBoneTransform(a);var lower=animator.GetBoneTransform(b);var hand=animator.GetBoneTransform(c);
            if(upper==null || lower==null || hand==null || grip==null)return;
            float first=Vector3.Distance(upper.position,lower.position),second=Vector3.Distance(lower.position,hand.position);
            Vector3 delta=grip.position-upper.position;float length=Mathf.Clamp(delta.magnitude,.01f,first+second-.002f);Vector3 forward=delta.normalized;
            Vector3 pole=Vector3.ProjectOnPlane(Vector3.down+animator.transform.forward*.3f,forward).normalized;
            float along=(first*first-second*second+length*length)/(2*length);float height=Mathf.Sqrt(Mathf.Max(0,first*first-along*along));
            Vector3 elbow=upper.position+forward*along+pole*height;
            upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,elbow-upper.position)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,grip.position-lower.position)*lower.rotation;
        }
        private void BuildHud()
        {
            canvasObject=new GameObject("CommanderHUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=30;
            var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
            var root=canvasObject.transform;
            healthRoot=UiKit.Node("CommanderHealth",root,UiKit.TopCenter,new Vector2(.5f,1),new Vector2(0,-112),new Vector2(620,55)).gameObject;
            title=UiKit.Text("BossName",healthRoot.transform,"CHỈ HUY · RPG-18",UiTheme.Display,23,UiTheme.Gold,TextAlignmentOptions.Center);
            UiKit.Place(title,UiKit.TopCenter,new Vector2(.5f,1),Vector2.zero,new Vector2(620,28));
            UiKit.Box("Track",healthRoot.transform,UiKit.BottomCenter,new Vector2(.5f,0),Vector2.zero,new Vector2(620,18),UiSprites.White,UiTheme.Ink);
            health=UiKit.Box("Fill",healthRoot.transform,UiKit.BottomCenter,new Vector2(.5f,0),new Vector2(0,3),new Vector2(614,12),UiSprites.White,UiTheme.Danger);
            health.type=Image.Type.Filled;health.fillMethod=Image.FillMethod.Horizontal;
            topLetter=UiKit.Box("TopLetterbox",root,UiKit.TopCenter,new Vector2(.5f,1),Vector2.zero,new Vector2(4000,65),UiSprites.White,Color.black).gameObject;
            bottomLetter=UiKit.Box("BottomLetterbox",root,UiKit.BottomCenter,new Vector2(.5f,0),Vector2.zero,new Vector2(4000,110),UiSprites.White,Color.black).gameObject;
            subtitle=UiKit.Text("BossSubtitle",root,"",UiTheme.Body,22,UiTheme.Paper,TextAlignmentOptions.Center);
            UiKit.Place(subtitle,UiKit.BottomCenter,new Vector2(.5f,0),new Vector2(0,16),new Vector2(1050,85));
            notice=UiKit.Text("BossReward",root,"",UiTheme.Body,19,UiTheme.Gold,TextAlignmentOptions.Center);
            UiKit.Place(notice,UiKit.TopCenter,new Vector2(.5f,1),new Vector2(0,-30),new Vector2(1100,45));
            foreach(var image in canvasObject.GetComponentsInChildren<Image>())image.raycastTarget=false;
            foreach(var text in canvasObject.GetComponentsInChildren<TMP_Text>())text.raycastTarget=false;
        }
        private void UpdateHud(bool cinematic)
        {
            healthRoot.SetActive(game.CommanderActive && !cinematic && game.State==CombatState.Playing);health.fillAmount=game.CommanderHealth/CombatGame.CommanderMaxHealth;
            bool showCombatSub=!cinematic && combatSubtitleTimer>0f && !string.IsNullOrEmpty(combatSubtitle) && (game.CommanderActive || game.State==CombatState.Defeat);
            topLetter.SetActive(cinematic);bottomLetter.SetActive(cinematic);subtitle.gameObject.SetActive(cinematic || showCombatSub);
            if(cinematic)
                subtitle.text=game.CommanderRetreating ? "“Bring up the armor. We finish this ourselves.”\nĐưa thiết giáp lên. Chúng ta sẽ tự kết thúc chuyện này.   <size=15>[Giữ SPACE để bỏ qua]</size>" : "“Break their line. Take the road!”\nChọc thủng phòng tuyến. Chiếm lấy con đường!   <size=15>[Giữ SPACE để bỏ qua]</size>";
            else if(showCombatSub)
                subtitle.text=combatSubtitle;
            bool reward=game.Run!=null && game.Run.CommanderRewardClaimed && (game.State==CombatState.Upgrade || game.State==CombatState.Shop);
            notice.text=reward ? "ĐÃ NHẬN RPG-18 · +20 TIẾP TẾ · +1 LƯỢT NÂNG CẤP\nNếu đủ 4 ô, RPG thay súng đang cầm. Đổi súng bằng con lăn/Q hoặc phím số." : "";
        }
        private void UpdateCamera(bool cinematic,float dt)
        {
            if(cameraView==null || view==null)return;
            if(cinematic)
            {
                if(!cameraOwned){view.EncounterCamera=true;cameraOwned=true;}
                if(dt<=0 || game.CommanderActor==null)return;
                Vector3 focus=game.CommanderActor.position+Vector3.up*.45f;
                float t=game.CommanderCinematicProgress;
                float angle=game.CommanderRetreating ? 110+45*t : 20+50*t;
                Vector3 offset=Quaternion.Euler(0,angle,0)*(bossRoot!=null ? bossRoot.forward : Vector3.forward)*(game.CommanderRetreating ? 4.8f : Mathf.Lerp(5,2.9f,t));
                cameraView.transform.position=focus+offset+Vector3.up*.5f;cameraView.transform.LookAt(focus);
            }
            else if(cameraOwned){view.EncounterCamera=false;cameraOwned=false;}
        }
        private void Explosion(Vector3 position,float radius,bool hostile)
        {
            if(hostile)lastHostileRocketFrame=Time.frameCount;
            if(view!=null && player!=null && Vector3.Distance(player.position,position)<12)view.Kick(.4f,.25f);
        }
        private void Smoke(Vector3 point)
        {
            if(rocketEffects!=null)rocketEffects.RetreatSmoke(point);
        }
        private void Clear()
        {
            if(playVictoryOnDefeat)
            {
                playVictoryOnDefeat=false;
                PlayBossVoice("Victory",VictorySubtitle,4);
            }
            else if(voice!=null)
            {
                voice.Stop();
                combatSubtitleTimer=0f;
                currentVoicePriority=0;
            }
            if(bossRoot!=null)bossRoot.gameObject.SetActive(false);
        }
        private void OnDestroy()
        {
            if(game!=null)
            {
                game.RocketExploded-=Explosion;game.TransientsCleared-=Clear;
                game.EnemyDamaged-=OnEnemyDamaged;game.PlayerDamaged-=OnPlayerDamaged;
            }
            if(cameraOwned && view!=null)view.EncounterCamera=false;
            if(graph.IsValid())graph.Destroy();
            if(canvasObject!=null)Destroy(canvasObject);if(bossRoot!=null)Destroy(bossRoot.gameObject);if(playerGun!=null)Destroy(playerGun.gameObject);
            if(bossWarningMaterial!=null)Destroy(bossWarningMaterial);
        }
    }
}
