using System;
using System.Collections;
using System.IO;
using GameJson;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace DiceTown
{
    public sealed class TownGame : MonoBehaviour
    {
        GameSession session;
        readonly ConfigStore<TownRules> configs=new ConfigStore<TownRules>(d=>new TownRules(d));
        TownWorld world;
        TownUI ui;
        bool compact=true,festival,busy,measuring;
        int width,height;
        string smokeDir,savePath;
        ComparisonResult lastComparison;
        AudioSource audioSource;
        AudioClip tick,win;
        float smokeStarted;
        void Start()
        {
            Application.targetFrameRate=60;Application.runInBackground=true;
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length-1;i++)if(args[i]=="--smoke-output")smokeDir=args[i+1];
            savePath=Path.Combine(Application.persistentDataPath,"town-save.json");
            session=new GameSession();
            if(smokeDir==null && File.Exists(savePath))
            {try{session=new GameSession(JsonConvert.DeserializeObject<TownSave>(File.ReadAllText(savePath)));}catch(Exception e){Debug.LogWarning("Save rejected: "+e.Message);}}
            ApplyConfig();
            world=new TownWorld(transform);
            ui=new TownUI(transform,()=>Roll(),Upgrade,NextTown,OpenComparison,SwitchMode,SwitchActivity,()=>ui.ConfirmReset(ResetGame));
            audioSource=gameObject.AddComponent<AudioSource>();audioSource.volume=.12f;tick=Tone(540,.055f);win=Tone(880,.18f);
            world.Token.position=TownWorld.Position(session.State.Position);world.RefreshBuildings(session.State.Levels);
            ui.Status.text="海风街区 · 新的旅程";Refresh();Layout();
            if(smokeDir!=null){Directory.CreateDirectory(smokeDir);smokeStarted=Time.realtimeSinceStartup;StartCoroutine(Smoke());}
        }
        void Update()
        {
            if(ui==null)return;
            if(Screen.width!=width||Screen.height!=height)Layout();
            if(Input.GetKeyDown(KeyCode.Space)&&ui.Modal==null)Roll();
            if(Input.GetKeyDown(KeyCode.Escape)&&!measuring)ui.CloseModal();
            if(smokeDir!=null&&Time.realtimeSinceStartup-smokeStarted>90){Debug.LogError("SMOKE TIMEOUT");Application.Quit(2);}
        }
        void Layout()
        {
            width=Screen.width;height=Screen.height;bool portrait=width/(float)height<1.15f;
            ui.Layout(portrait);world.Camera.rect=portrait?new Rect(0,.40f,1,.50f):new Rect(0,.20f,.74f,.69f);
            world.Camera.orthographicSize=portrait?7.3f:6.9f;
        }
        void ApplyConfig()
        {
            int version=festival?2:1;
            string name="Config/town_v"+version;
            IConfigData data;
            var asset=Resources.Load<TextAsset>(name+(compact?"_packed":""));
            if(asset==null)throw new FileNotFoundException("Missing built config: "+name);
            try{data=compact?(IConfigData)CompactConfig.Load(asset.bytes):ObjectConfig.Load(asset.text);}
            finally{Resources.UnloadAsset(asset);}
            if(!configs.TryPublish(data,out string error))throw new InvalidDataException(error);
        }
        void SwitchMode()
        {
            if(measuring)return;
            compact=!compact;
            try{ApplyConfig();ui.Status.text=compact?"已切换 Game-Json · 进度保留":"已切换 Object JSON · 进度保留";}
            catch(Exception e){compact=!compact;ui.Status.text="切换失败："+e.Message;}
            Refresh();
        }
        void SwitchActivity()
        {
            if(measuring)return;
            festival=!festival;
            try{ApplyConfig();ui.Status.text=festival?"丰收节开幕 · 后续格子奖励翻倍":"日常营业恢复";}
            catch(Exception e){festival=!festival;ui.Status.text="活动未切换："+e.Message;}
            Refresh();
        }
        void Roll(int first=0,int second=0)
        {
            if(busy||measuring||ui.Modal!=null)return;
            if(session.State.Dice==0){session.Supply();Save();Refresh();ui.Status.text="补给到达 · 获得 12 次骰子";return;}
            var turn=session.BeginRoll(configs.Current,first==0?UnityEngine.Random.Range(1,7):first,second==0?UnityEngine.Random.Range(1,7):second);
            if(turn==null)return;
            busy=true;Save();Refresh();StartCoroutine(PlayTurn(turn));
        }
        IEnumerator PlayTurn(PendingTurn turn)
        {
            for(int i=0;i<9;i++){ui.SetDice(UnityEngine.Random.Range(1,7),UnityEngine.Random.Range(1,7));audioSource.PlayOneShot(tick);yield return new WaitForSeconds(.055f);}
            ui.SetDice(turn.Die1,turn.Die2);yield return world.Move(turn);
            session.CompleteRoll(turn);Save();
            ui.Status.text=TranslateTile(turn.Name)+"  "+(turn.Coins>=0?"+":"")+turn.Coins+" 金币"+(turn.Dice>0?"  +"+turn.Dice+" 骰子":"")+" · "+(turn.Version==2?"丰收节":"日常");
            audioSource.PlayOneShot(win);Refresh();if(turn.Coins>0)yield return world.Burst();busy=false;Refresh();
        }
        static string TranslateTile(string name)
        {return name=="STATION"?"车站分红":name=="SUPPLY"?"补给站":name=="CHANCE"?"街区事件":name=="START"?"出发广场":"街区收益";}
        void Upgrade(int building)
        {
            if(busy||!session.Upgrade(configs.Current,building))return;
            Save();world.RefreshBuildings(session.State.Levels);audioSource.PlayOneShot(win);ui.Status.text="建筑升级 · 每次正收益增加 5 金币";Refresh();
        }
        void NextTown()
        {
            if(!session.NextTown())return;
            Save();world.RefreshBuildings(session.State.Levels);world.Token.position=TownWorld.Position(0);ui.Status.text="抵达新的小镇 · +28 骰子，+180 金币";Refresh();
        }
        void ResetGame(){if(busy)return;session=new GameSession();Save();world.RefreshBuildings(session.State.Levels);world.Token.position=TownWorld.Position(0);ui.Status.text="新的旅程开始了";Refresh();}
        void Refresh()
        {
            if(ui==null)return;var s=session.State;
            ui.Balance.text="金币  "+s.Coins.ToString("N0");ui.Dice.text="骰子  "+s.Dice;ui.Shield.text="护盾  "+s.Shields+" / 3";
            int built=s.Levels[0]+s.Levels[1]+s.Levels[2];ui.Town.text="小镇 "+(s.Town+1).ToString("D2")+"     "+built+" / 15";
            ui.SetBackend(compact);ui.EventLabel.text=festival?"丰收节  ·  奖励 ×2":"日常营业";
            ui.RollButton.interactable=!busy&&!measuring;ui.RollLabel.text=busy?"前进中…":s.Dice==0?"领取补给":"掷骰子";
            string[] names={"街角咖啡馆","城市花园","假日酒店"};
            for(int b=0;b<3;b++)
            {
                int level=s.Levels[b];ui.Buildings[b].text=names[b]+"\n<size=14>等级 "+level+" / 5</size>";
                int cost=level<5?configs.Current.Cost(s.Town,b,level+1):0;
                ui.BuildButtons[b].GetComponentInChildren<Text>().text=level==5?"已完成":"建造  ·  "+cost;
                ui.BuildButtons[b].interactable=!busy&&level<5&&s.Coins>=cost;
            }
            ui.NextButton.gameObject.SetActive(built==15&&!busy&&s.Town<TownRules.TownCount-1);
        }
        void Save()
        {
            if(smokeDir!=null)return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath));
                string temp=savePath+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(session.State));
                if(File.Exists(savePath))File.Replace(temp,savePath,null);else File.Move(temp,savePath);
            }
            catch(Exception e){Debug.LogError("Save failed: "+e.Message);if(ui!=null)ui.Status.text="存档写入失败，当前进度仅保留在内存";}
        }
        void OpenComparison()
        {if(busy||measuring)return;ui.OpenComparison(()=>{if(!measuring)ui.CloseModal();},()=>StartCoroutine(Measure()),RejectInvalid);StartCoroutine(Measure());}
        IEnumerator Measure()
        {
            if(measuring)yield break;measuring=true;ui.BeginMeasurement();Refresh();yield return null;
            var source=Resources.Load<TextAsset>("Config/town_v1");var packed=Resources.Load<TextAsset>("Config/town_v1_packed");
            try
            {
                lastComparison=ConfigComparison.Run(source.text,packed.bytes,new UnityAllocationMeter());
                lastComparison.EnvironmentLabel="Unity "+Application.unityVersion+" / "+(Application.isEditor?"Editor":Debug.isDebugBuild?"Windows Development / Mono":"Windows Release / Mono");
                lastComparison.Processor=SystemInfo.processorType;
                ui.ShowMetrics(lastComparison,lastComparison.EnvironmentLabel);
                string output=smokeDir??Application.persistentDataPath;
                File.WriteAllText(Path.Combine(output,"comparison.json"),JsonConvert.SerializeObject(lastComparison,Formatting.Indented));
            }
            catch(Exception e){ui.ShowComparisonError("测量失败："+e.Message);Debug.LogException(e);}
            finally{Resources.UnloadAsset(source);Resources.UnloadAsset(packed);measuring=false;Refresh();}
        }
        void RejectInvalid()
        {
            if(measuring)return;
            var before=configs.Current;var asset=Resources.Load<TextAsset>("Config/town_v1_packed");byte[] bytes=asset.bytes;Resources.UnloadAsset(asset);bytes[bytes.Length-1]^=1;
            try{CompactConfig.Load(bytes);ui.ShowComparisonError("错误：损坏数据被接受");}
            catch(Exception e){ui.ShowComparisonError("<b>损坏配置已拒绝</b>\n\n"+e.Message+"\n\n当前游戏配置保留："+(ReferenceEquals(before,configs.Current)?"是":"否")+"\n金币、建筑与棋盘进度均未改变。");}
        }
        IEnumerator Smoke()
        {
            Screen.SetResolution(1280,799,false);yield return new WaitForSeconds(.5f);
            Screen.SetResolution(1280,800,false);
            yield return new WaitForSeconds(1);
            ScreenCapture.CaptureScreenshot(Path.Combine(smokeDir,"01-town.png"));yield return new WaitForSeconds(.5f);
            Roll(3,3);yield return new WaitForSeconds(.2f);SwitchActivity();
            while(busy)yield return null;
            if(session.State.Coins!=350||session.State.Position!=6||configs.Current.Version!=2) { Debug.LogError("SMOKE: in-flight settlement failed");Application.Quit(3);yield break; }
            Upgrade(0);Upgrade(1);SwitchMode();Roll(6,6);while(busy)yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(smokeDir,"02-built-town.png"));yield return new WaitForSeconds(.5f);
            OpenComparison();while(measuring)yield return null;
            if(lastComparison==null||!lastComparison.SameResults){Application.Quit(4);yield break;}
            if(Debug.isDebugBuild&&(!lastComparison.AllocationCounterAvailable||!lastComparison.ZeroAllocationVerified||lastComparison.ObjectJson.QueryAllocationEvents!=0))
            {Debug.LogError("SMOKE: allocation calibration or query verification failed");Application.Quit(5);yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(smokeDir,"03-comparison.png"));yield return new WaitForSeconds(.5f);
            ui.SelectMetricTab(1);ScreenCapture.CaptureScreenshot(Path.Combine(smokeDir,"03-memory.png"));yield return new WaitForSeconds(.5f);
            ui.SelectMetricTab(2);ScreenCapture.CaptureScreenshot(Path.Combine(smokeDir,"03-allocation.png"));yield return new WaitForSeconds(.5f);
            Screen.SetResolution(720,1080,false);yield return new WaitForSeconds(1);
            ScreenCapture.CaptureScreenshot(Path.Combine(smokeDir,"03-allocation-portrait.png"));yield return new WaitForSeconds(.5f);
            ui.SelectMetricTab(0);ScreenCapture.CaptureScreenshot(Path.Combine(smokeDir,"03-access-portrait.png"));yield return new WaitForSeconds(.5f);
            Screen.SetResolution(1280,800,false);yield return new WaitForSeconds(1);
            RejectInvalid();ScreenCapture.CaptureScreenshot(Path.Combine(smokeDir,"04-rejected.png"));yield return new WaitForSeconds(.5f);ui.CloseModal();
            Screen.SetResolution(720,1080,false);yield return new WaitForSeconds(1);ScreenCapture.CaptureScreenshot(Path.Combine(smokeDir,"05-portrait.png"));yield return new WaitForSeconds(.5f);
            File.WriteAllText(Path.Combine(smokeDir,"smoke-result.json"),JsonConvert.SerializeObject(new{Passed=true,session.State.Coins,session.State.Position,session.State.Turns,ActivityVersion=configs.Current.Version,Unity=Application.unityVersion,Runtime=SystemInfo.graphicsDeviceName},Formatting.Indented));
            Debug.Log("SMOKE PASSED");Application.Quit(0);
        }
        static AudioClip Tone(float frequency,float seconds)
        {int size=(int)(44100*seconds);var data=new float[size];for(int i=0;i<size;i++)data[i]=Mathf.Sin(2*Mathf.PI*frequency*i/44100)*.4f*(1-i/(float)size);var c=AudioClip.Create("Town tone",size,1,44100,false);c.SetData(data,0);return c;}
        void OnApplicationQuit(){if(session!=null)Save();}
        void OnDestroy(){world?.Dispose();if(tick!=null)Destroy(tick);if(win!=null)Destroy(win);}
    }
}
