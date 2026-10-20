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
        public SkinnedCharacterProfile Profile;
        public GameObject Launcher;
        private CombatGame game;
        private Transform player,bossRoot,bossGun,playerGun;
        private Animator bossAnimator,heroAnimator;
        private Camera cameraView;
        private CombatThirdPersonCamera view;
        private bool cameraOwned,wasCinematic,wasRetreat;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private Vector3 lastBoss;
        private GameObject canvasObject,healthRoot,topLetter,bottomLetter;
        private Image health;
        private TMP_Text title,subtitle,notice;
        private AudioSource voice;
        private Material bossWarningMaterial;
        private AudioClip launchSound,explosionSound;
        private readonly List<GameObject> flashes=new List<GameObject>();
        private readonly List<float> flashAges=new List<float>();
        private void Start()
        {
            game=GetComponent<CombatGame>();player=transform.Find("Player");cameraView=Camera.main;
            if(cameraView!=null)view=cameraView.GetComponent<CombatThirdPersonCamera>();
            voice=gameObject.AddComponent<AudioSource>();voice.spatialBlend=0;voice.volume=.9f;
            launchSound=Noise("RPGLaunch",.3f,120);explosionSound=Noise("RPGExplosion",.8f,55);
            game.RocketExploded+=Explosion;game.LauncherFired+=Launch;game.TransientsCleared+=Clear;
            BuildHud();
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
            UpdateHeroLauncher();UpdateCamera(cinematic,dt);UpdateHud(cinematic);
            if(game.State==CombatState.Paused)voice.Pause();else voice.UnPause();
            if(cinematic && (!wasCinematic || wasRetreat!=game.CommanderRetreating))
            {
                voice.Stop();voice.clip=Resources.Load<AudioClip>("CommanderVoice/"+(game.CommanderRetreating ? "Retreat" : "Intro"));
                if(voice.clip!=null)voice.Play();
                if(game.CommanderRetreating) Smoke(game.CommanderActor.position);
            }
            if(!cinematic && wasCinematic)voice.Stop();
            wasCinematic=cinematic;wasRetreat=game.CommanderRetreating;
            for(int i=flashes.Count-1;i>=0;i--)
            {
                if(flashes[i]==null){flashes.RemoveAt(i);flashAges.RemoveAt(i);continue;}
                flashAges[i]+=dt;float age=flashAges[i];
                flashes[i].transform.localScale=Vector3.one*(.3f+age*7);
                if(age>.55f){Destroy(flashes[i].GetComponent<Renderer>().sharedMaterial);Destroy(flashes[i]);flashes.RemoveAt(i);flashAges.RemoveAt(i);}
            }
        }
        private void UpdateHeroLauncher()
        {
            if(Launcher==null)return;
            if(playerGun==null){playerGun=Instantiate(Launcher,transform,false).transform;playerGun.name="PlayerRPG18";}
            bool active=game.LauncherEquipped && game.State!=CombatState.Menu && game.State!=CombatState.Defeat && !game.CommanderCinematic;
            playerGun.gameObject.SetActive(active);if(!active)return;
            if(heroAnimator==null)heroAnimator=player.GetComponentInChildren<Animator>();
            if(view!=null && view.FirstPersonWeight>.5f && cameraView!=null)
            {
                playerGun.position=cameraView.transform.TransformPoint(new Vector3(.22f,-.22f,.48f));
                playerGun.rotation=cameraView.transform.rotation;
            }
            else if(heroAnimator!=null)FitLauncher(heroAnimator,playerGun,game.PlayerAimDirection);
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
            topLetter.SetActive(cinematic);bottomLetter.SetActive(cinematic);subtitle.gameObject.SetActive(cinematic);
            subtitle.text=game.CommanderRetreating ? "“Bring up the armor. We finish this ourselves.”\nĐưa thiết giáp lên. Chúng ta sẽ tự kết thúc chuyện này.   <size=15>[Giữ SPACE để bỏ qua]</size>" : "“Break their line. Take the road!”\nChọc thủng phòng tuyến. Chiếm lấy con đường!   <size=15>[Giữ SPACE để bỏ qua]</size>";
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
        private void Launch(bool hostile){if(launchSound!=null)AudioSource.PlayClipAtPoint(launchSound,hostile && game.CommanderActor!=null ? game.CommanderActor.position : player.position,.8f);}
        private void Explosion(Vector3 position,float radius,bool hostile)
        {
            var flash=GameObject.CreatePrimitive(PrimitiveType.Sphere);flash.name="RPGBlast";flash.transform.position=position;
            var collider=flash.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",new Color(2.8f,.65f,.1f));flash.GetComponent<Renderer>().sharedMaterial=material;
            flashes.Add(flash);flashAges.Add(0);Smoke(position);
            if(explosionSound!=null)AudioSource.PlayClipAtPoint(explosionSound,position,1);
            if(view!=null && player!=null && Vector3.Distance(player.position,position)<12)view.Kick(.4f,.25f);
        }
        private void Smoke(Vector3 point)
        {
            var go=new GameObject("RPGSmoke");go.transform.position=point;var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=false;main.duration=.4f;main.startLifetime=1.4f;main.startSpeed=1.4f;main.startSize=1;main.startColor=new Color(.24f,.23f,.2f,.6f);main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=ps.emission;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,12)});
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.4f;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=ProceduralArt.Particle("CommanderSmoke",true,new Color(.24f,.23f,.2f,.6f));
            ps.Play();Destroy(go,2);
        }
        private static AudioClip Noise(string name,float duration,float bass)
        {
            const int rate=22050;var data=new float[(int)(duration*rate)];var rng=new System.Random(41);
            float filtered=0;
            for(int i=0;i<data.Length;i++){float t=(float)i/rate;filtered=Mathf.Lerp(filtered,(float)rng.NextDouble()*2-1,.13f);data[i]=(filtered*.65f+Mathf.Sin(t*bass*Mathf.PI*2)*.35f)*Mathf.Exp(-t*6);}
            var clip=AudioClip.Create(name,data.Length,1,rate,false);clip.SetData(data,0);return clip;
        }
        private void Clear(){if(voice!=null)voice.Stop();if(bossRoot!=null)bossRoot.gameObject.SetActive(false);}
        private void OnDestroy()
        {
            if(game!=null){game.RocketExploded-=Explosion;game.LauncherFired-=Launch;game.TransientsCleared-=Clear;}
            if(cameraOwned && view!=null)view.EncounterCamera=false;
            if(graph.IsValid())graph.Destroy();
            if(canvasObject!=null)Destroy(canvasObject);if(bossRoot!=null)Destroy(bossRoot.gameObject);if(playerGun!=null)Destroy(playerGun.gameObject);
            foreach(var flash in flashes)if(flash!=null){Destroy(flash.GetComponent<Renderer>().sharedMaterial);Destroy(flash);}
            if(bossWarningMaterial!=null)Destroy(bossWarningMaterial);
            if(launchSound!=null)Destroy(launchSound);if(explosionSound!=null)Destroy(explosionSound);
        }
    }
}
