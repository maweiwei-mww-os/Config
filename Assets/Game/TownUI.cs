using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace DiceTown
{
    public sealed class TownUI
    {
        public readonly Canvas Canvas;
        public Text Balance,Dice,Shield,Town,Status,RollLabel,EventLabel;
        public Button RollButton, NextButton;
        public readonly Text[] Buildings=new Text[3];
        public readonly Button[] BuildButtons=new Button[3];
        public readonly Image[,] Pips=new Image[2,9];
        public GameObject Modal;
        public Text Comparison;
        public Text ComparisonNote;
        public GameObject MetricGrid;
        readonly Text[,] metricValues=new Text[6,3];
        readonly Text[] headlines=new Text[3];
        readonly Button[] metricTabs=new Button[3];
        ComparisonResult comparisonResult;
        string comparisonRuntime;
        int metricTab;
        Button objectMode,compactMode;
        bool selectedCompact=true;
        readonly RectTransform side;
        readonly RectTransform[] cards=new RectTransform[3];
        readonly Font font;
        static readonly Color Ink=new Color(.10f,.22f,.25f),Teal=new Color(.08f,.49f,.43f),Muted=new Color(.40f,.49f,.52f),Coral=new Color(.90f,.32f,.25f);
        readonly Sprite dot;
        public TownUI(Transform parent,Action roll,Action<int> build,Action next,Action compare,Action mode,Action activity,Action reset)
        {
            font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},32);
            var texture=new Texture2D(32,32,TextureFormat.RGBA32,false);var pixels=new Color[32*32];
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)pixels[y*32+x]=new Color(1,1,1,Mathf.Clamp01(15.5f-Vector2.Distance(new Vector2(x,y),new Vector2(15.5f,15.5f))));
            texture.SetPixels(pixels);texture.Apply();dot=Sprite.Create(texture,new Rect(0,0,32,32),new Vector2(.5f,.5f));
            var go=new GameObject("Town interface",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));go.transform.SetParent(parent);
            Canvas=go.GetComponent<Canvas>();Canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440,900);scaler.matchWidthOrHeight=.5f;
            if(UnityEngine.Object.FindObjectOfType<EventSystem>()==null)new GameObject("Input",typeof(EventSystem),typeof(StandaloneInputModule));
            var header=Panel("Header",Canvas.transform,new Vector2(0,.90f),Vector2.one,new Color(.98f,.99f,.98f));
            Label(header,"骰子小镇",32,new Vector2(.035f,.15f),new Vector2(.28f,.85f),Ink,TextAnchor.MiddleLeft);
            Balance=Label(header,"",25,new Vector2(.37f,.12f),new Vector2(.59f,.88f),Ink);
            Dice=Label(header,"",23,new Vector2(.60f,.12f),new Vector2(.78f,.88f),Ink);
            Shield=Label(header,"",20,new Vector2(.79f,.12f),new Vector2(.98f,.88f),Teal);
            side=Rect("Town panel",Canvas.transform,new Vector2(.75f,.19f),new Vector2(.975f,.87f));
            Town=Label(side,"",25,new Vector2(0,.84f),Vector2.one,Ink,TextAnchor.MiddleLeft);
            Color[] accents={Coral,Teal,new Color(.76f,.49f,.08f)};
            for(int i=0;i<3;i++)
            {
                int index=i; cards[i]=Panel("Building card "+i,side,new Vector2(0,.59f-i*.27f),new Vector2(1,.81f-i*.27f),Color.white);
                Panel("Accent",cards[i],Vector2.zero,new Vector2(.018f,1),accents[i]);
                Buildings[i]=Label(cards[i],"",19,new Vector2(.07f,.43f),new Vector2(.95f,.97f),Ink,TextAnchor.MiddleLeft);
                BuildButtons[i]=Button(cards[i],"",new Vector2(.07f,.08f),new Vector2(.94f,.39f),()=>build(index),accents[i]);
            }
            var footer=Panel("Controls",Canvas.transform,new Vector2(0,0),new Vector2(1,.14f),Color.white);
            objectMode=Button(footer,"JSON",new Vector2(.025f,.53f),new Vector2(.13f,.88f),()=>{if(selectedCompact)mode();},Teal);
            compactMode=Button(footer,"Game-Json",new Vector2(.133f,.53f),new Vector2(.245f,.88f),()=>{if(!selectedCompact)mode();},Teal);
            objectMode.GetComponentInChildren<Text>().fontSize=15;compactMode.GetComponentInChildren<Text>().fontSize=15;
            var eventButton=Button(footer,"",new Vector2(.025f,.10f),new Vector2(.245f,.45f),activity,new Color(.97f,.90f,.71f));EventLabel=eventButton.GetComponentInChildren<Text>();EventLabel.color=Ink;EventLabel.fontSize=16;
            for(int d=0;d<2;d++)
            {
                var die=Panel("Die "+d,footer,new Vector2(.30f+d*.075f,.24f),new Vector2(.36f+d*.075f,.81f),new Color(.92f,.94f,.93f));
                for(int p=0;p<9;p++){var pip=Panel("Pip",die,new Vector2(.15f+p%3*.25f,.15f+p/3*.25f),new Vector2(.30f+p%3*.25f,.30f+p/3*.25f),Ink).GetComponent<Image>();pip.sprite=dot;Pips[d,p]=pip;}
            }
            RollButton=Button(footer,"掷骰子",new Vector2(.48f,.15f),new Vector2(.71f,.86f),roll,Coral);RollLabel=RollButton.GetComponentInChildren<Text>();RollLabel.fontSize=27;
            Button(footer,"性能实测",new Vector2(.76f,.53f),new Vector2(.96f,.88f),compare,new Color(.15f,.28f,.31f));
            Button(footer,"重新开始",new Vector2(.76f,.10f),new Vector2(.96f,.45f),reset,new Color(.86f,.92f,.92f)).GetComponentInChildren<Text>().color=Ink;
            Status=Label(Canvas.transform,"",18,new Vector2(.025f,.155f),new Vector2(.71f,.205f),Ink);
            NextButton=Button(Canvas.transform,"小镇完成 · 前往下一站",new Vector2(.21f,.76f),new Vector2(.53f,.84f),next,Teal);NextButton.gameObject.SetActive(false);
            SetDice(1,1);
        }
        public void Layout(bool portrait)
        {
            side.anchorMin=portrait?new Vector2(.025f,.20f):new Vector2(.75f,.19f);
            side.anchorMax=portrait?new Vector2(.975f,.40f):new Vector2(.975f,.87f);
            Town.rectTransform.anchorMin=new Vector2(0,portrait?.82f:.84f);
            Town.fontSize=portrait?18:25;
            for(int i=0;i<3;i++)
            {
                cards[i].anchorMin=portrait?new Vector2(i*.34f,0):new Vector2(0,.59f-i*.27f);
                cards[i].anchorMax=portrait?new Vector2(i*.34f+.32f,.78f):new Vector2(1,.81f-i*.27f);
                Buildings[i].fontSize=portrait?15:19;
                BuildButtons[i].GetComponentInChildren<Text>().fontSize=portrait?15:18;
            }
            Status.rectTransform.anchorMax=new Vector2(portrait?.975f:.71f,.195f);
            NextButton.GetComponent<RectTransform>().anchorMin=portrait?new Vector2(.25f,.43f):new Vector2(.21f,.76f);
            NextButton.GetComponent<RectTransform>().anchorMax=portrait?new Vector2(.75f,.48f):new Vector2(.53f,.84f);
        }
        public void SetDice(int a,int b)
        {
            int[][] patterns={new[]{4},new[]{0,8},new[]{0,4,8},new[]{0,2,6,8},new[]{0,2,4,6,8},new[]{0,2,3,5,6,8}};
            for(int d=0;d<2;d++)for(int p=0;p<9;p++)Pips[d,p].enabled=Array.IndexOf(patterns[(d==0?a:b)-1],p)>=0;
        }
        public void OpenComparison(Action close,Action rerun,Action invalid)
        {
            if(Modal!=null)return;
            Modal=Panel("Comparison overlay",Canvas.transform,Vector2.zero,Vector2.one,new Color(.06f,.13f,.16f,.82f)).gameObject;
            var sheet=Panel("Comparison",Modal.transform,new Vector2(.04f,.06f),new Vector2(.96f,.94f),new Color(.98f,.99f,.98f));
            Label(sheet,"配置性能实测",28,new Vector2(.05f,.91f),new Vector2(.75f,.99f),Ink,TextAnchor.MiddleLeft);
            Button(sheet,"关闭",new Vector2(.82f,.92f),new Vector2(.95f,.975f),close,Teal);
            comparisonResult=null;metricTab=0;
            string[] titles={"访问速度","内存与加载","零分配验证"};
            for(int i=0;i<3;i++)
            {
                int index=i;float x=.05f+i*.305f;
                headlines[i]=Label(sheet,"",18,new Vector2(x,.765f),new Vector2(x+.29f,.905f),i==2?Teal:Ink,TextAnchor.MiddleLeft);
                metricTabs[i]=Button(sheet,titles[i],new Vector2(x,.69f),new Vector2(x+.29f,.75f),()=>SelectMetricTab(index),Teal);
            }
            Comparison=Label(sheet,"正在采样…",17,new Vector2(.05f,.235f),new Vector2(.95f,.285f),Ink,TextAnchor.MiddleLeft);
            Comparison.horizontalOverflow=HorizontalWrapMode.Wrap;Comparison.resizeTextForBestFit=true;Comparison.resizeTextMinSize=14;Comparison.resizeTextMaxSize=17;
            MetricGrid=Rect("Measurements",sheet,new Vector2(.05f,.30f),new Vector2(.95f,.675f)).gameObject;
            for(int r=0;r<6;r++)for(int c=0;c<3;c++)
            {
                float x=c==0?0:c==1?.34f:.64f;float end=c==0?.32f:c==1?.60f:1;
                metricValues[r,c]=Label(MetricGrid.transform,"",18,new Vector2(x,1-(r+1)/6f),new Vector2(end,1-r/6f),r==0?Muted:c==2?Teal:Ink,c==0?TextAnchor.MiddleLeft:TextAnchor.MiddleRight);
                metricValues[r,c].resizeTextForBestFit=true;metricValues[r,c].resizeTextMinSize=14;metricValues[r,c].resizeTextMaxSize=18;
            }
            ComparisonNote=Label(sheet,"",14,new Vector2(.05f,.125f),new Vector2(.95f,.23f),Muted,TextAnchor.MiddleLeft);
            ComparisonNote.resizeTextForBestFit=true;ComparisonNote.resizeTextMinSize=12;ComparisonNote.resizeTextMaxSize=14;
            Button(sheet,"重新测量",new Vector2(.05f,.035f),new Vector2(.46f,.105f),rerun,Teal);
            Button(sheet,"载入损坏配置",new Vector2(.53f,.035f),new Vector2(.95f,.105f),invalid,new Color(.66f,.27f,.24f));
        }
        public void SetBackend(bool compact)
        {
            selectedCompact=compact;
            objectMode.GetComponent<Image>().color=compact?new Color(.86f,.92f,.92f):Teal;
            compactMode.GetComponent<Image>().color=compact?Teal:new Color(.86f,.92f,.92f);
            objectMode.GetComponentInChildren<Text>().color=compact?Ink:Color.white;
            compactMode.GetComponentInChildren<Text>().color=compact?Color.white:Ink;
        }
        public void ShowMetrics(ComparisonResult result,string runtime)
        {
            comparisonResult=result;comparisonRuntime=runtime;
            headlines[0].text="混合读取吞吐倍率\n<size=28>"+result.QuerySpeedup.ToString("F2")+"×</size>\n"+result.Compact.NanosecondsPerQuery.ToString("F1")+" ns / 次";
            headlines[1].text="配置常驻内存节省\n<size=28>"+(double.IsNaN(result.MemorySavingPercent)?"未验证":result.MemorySavingPercent.ToString("F1")+"%")+"</size>\n"+Memory(result.Compact.RetainedBytes)+" / "+result.Compact.TotalRows.ToString("N0")+" 条";
            headlines[2].text="读取新增内存\n<size=28>"+Allocation(result.Compact.QueryAllocatedBytes)+"</size>\n20 万次 · "+(result.ZeroAllocationVerified?"零分配通过":result.AllocationCounterAvailable?"存在分配":"尚未验证");
            headlines[2].color=result.ZeroAllocationVerified?Teal:new Color(.65f,.36f,.08f);
            SelectMetricTab(metricTab);
        }
        public void BeginMeasurement()
        {
            comparisonResult=null;MetricGrid.SetActive(false);ComparisonNote.text="";
            Comparison.rectTransform.anchorMin=new Vector2(.05f,.235f);Comparison.rectTransform.anchorMax=new Vector2(.95f,.285f);Comparison.text="正在采样…";
            foreach(var headline in headlines)headline.text="";
        }
        public void SelectMetricTab(int tab)
        {
            metricTab=tab;
            for(int i=0;i<3;i++) {
                metricTabs[i].GetComponent<Image>().color=i==tab?Teal:new Color(.86f,.92f,.92f);
                metricTabs[i].GetComponentInChildren<Text>().color=i==tab?Color.white:Ink;
            }
            if(comparisonResult==null)return;
            var result=comparisonResult;
            MetricGrid.SetActive(true);Comparison.rectTransform.anchorMin=new Vector2(.05f,.235f);Comparison.rectTransform.anchorMax=new Vector2(.95f,.285f);
            Comparison.text=result.SameResults?"<color=#147b66>5 项读取结果一致</color>  ·  同一配置 / 同一 ID 序列":"<color=#b73324>读取结果不一致</color>";
            var a=result.ObjectJson;var b=result.Compact;
            string[] names,left,right;string note;
            if(tab==0) {
                names=new[]{"每项 20 万次","ID 定位","按 ID 读数值","按 ID 读字符串","按 ID 遍历列表","混合读取"};
                left=new string[6];right=new string[6];left[0]="Object JSON";right[0]="Game-Json / 倍率";
                for(int i=0;i<5;i++) {
                    left[i+1]=a.Access[i].Milliseconds.ToString("F2")+" ms";
                    right[i+1]=b.Access[i].Milliseconds.ToString("F2")+" ms · "+(b.Access[i].Milliseconds>0?(a.Access[i].Milliseconds/b.Access[i].Milliseconds).ToString("F2")+"×":"未验证");
                }
                note="预热后 5 轮取中位数；2,880 条奖励。混合读取含定位、数值、字符串长度与列表求和。\n计时不含分配检测；倍率仅代表这组配置与访问负载，不代表游戏帧率。";
            } else if(tab==1) {
                names=new[]{"7,560 条配置","配置常驻（估计）","均摊每条（估计）","输入文件体积","加载与校验","加载分配次数"};
                left=new[]{"Object JSON",Memory(a.RetainedBytes),PerRow(a),Memory(result.JsonBytes),a.LoadMilliseconds.ToString("F2")+" ms",Count(a.LoadAllocationEvents)};
                right=new[]{"Game-Json",Memory(b.RetainedBytes),PerRow(b),Memory(result.CompactBytes),b.LoadMilliseconds.ToString("F2")+" ms",Count(b.LoadAllocationEvents)};
                note="常驻：完整 GC 前后托管堆差值，排除输入、游戏资源；不是进程总内存。\n加载为单次观测，不含磁盘 I/O。加载本身允许分配，零分配仅针对预热后的读取。";
            } else {
                names=new[]{"20 万次混合读取","读取新增内存","读取分配次数","进程 GC 回收次数","校准：创建 4 KB 数组","校准：空操作"};
                left=new[]{"Object JSON",Allocation(a.QueryAllocatedBytes),Count(a.QueryAllocationEvents),Count(a.QueryCollections),Count(result.PositiveControlEvents),Count(result.EmptyControlEvents)};
                right=new[]{"Game-Json",Allocation(b.QueryAllocatedBytes),Count(b.QueryAllocationEvents),Count(b.QueryCollections),Count(result.PositiveControlEvents),Count(result.EmptyControlEvents)};
                note=result.AllocationCounterAvailable?"校准通过；0 次 GC.Alloc 分配事件证明本次读取新增 0 B，非零事件不推算字节数。\n两种后端均可零分配；回收 0 次不等于零分配。仅测主线程读取，不包含加载、动画、UI、存档。":"本运行环境未通过分配检测校准，不能证明零 GC。\n性能验证版单独采样，不将不可用的检测结果标为通过。";
            }
            for(int r=0;r<6;r++){metricValues[r,0].text=names[r];metricValues[r,1].text=left[r];metricValues[r,2].text=right[r];}
            ComparisonNote.text=note+"\n"+comparisonRuntime;
        }
        public void ShowComparisonError(string message)
        {comparisonResult=null;MetricGrid.SetActive(false);ComparisonNote.text="";Comparison.rectTransform.anchorMin=new Vector2(.05f,.30f);Comparison.rectTransform.anchorMax=new Vector2(.95f,.675f);Comparison.text=message;}
        static string Count(long value)=>value<0?"未验证":value.ToString("N0")+" 次";
        static string PerRow(BackendMeasurement value)=>value.RetainedBytes<0||value.TotalRows<=0?"未验证":(value.RetainedBytes/(double)value.TotalRows).ToString("F1")+" B";
        static string Memory(long value)=>value<0?"噪声过大":(value/1024.0).ToString("F1")+" KB";
        static string Allocation(long value)=>value<0?"未验证":value<1024?value+" B":Memory(value);
        public void CloseModal(){if(Modal!=null)UnityEngine.Object.Destroy(Modal);Modal=null;}
        public void ConfirmReset(Action confirm)
        {
            if(Modal!=null)return;
            Modal=Panel("Reset overlay",Canvas.transform,Vector2.zero,Vector2.one,new Color(.06f,.13f,.16f,.82f)).gameObject;
            var p=Panel("Reset dialog",Modal.transform,new Vector2(.18f,.34f),new Vector2(.82f,.66f),Color.white);
            Label(p,"重新开始这段旅程？",25,new Vector2(.05f,.55f),new Vector2(.95f,.90f),Ink);
            Button(p,"取消",new Vector2(.08f,.12f),new Vector2(.46f,.39f),CloseModal,Teal);
            Button(p,"重新开始",new Vector2(.54f,.12f),new Vector2(.92f,.39f),()=>{CloseModal();confirm();},Coral);
        }
        static RectTransform Rect(string name,Transform parent,Vector2 min,Vector2 max)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=min;r.anchorMax=max;r.offsetMin=Vector2.zero;r.offsetMax=Vector2.zero;return r;}
        static RectTransform Panel(string name,Transform parent,Vector2 min,Vector2 max,Color color)
        {var r=Rect(name,parent,min,max);r.gameObject.AddComponent<Image>().color=color;return r;}
        Text Label(Transform parent,string content,int size,Vector2 min,Vector2 max,Color color,TextAnchor align=TextAnchor.MiddleCenter)
        {var t=Rect(content.Length>0?content:"Label",parent,min,max).gameObject.AddComponent<Text>();t.font=font;t.fontSize=size;t.text=content;t.color=color;t.alignment=align;t.raycastTarget=false;t.supportRichText=true;return t;}
        Button Button(Transform parent,string caption,Vector2 min,Vector2 max,Action action,Color color)
        {
            var r=Panel(caption,parent,min,max,color);var b=r.gameObject.AddComponent<Button>();b.targetGraphic=r.GetComponent<Image>();b.onClick.AddListener(()=>action());
            var c=b.colors;c.highlightedColor=new Color(1,1,1,.86f);c.pressedColor=new Color(.75f,.80f,.80f);c.disabledColor=new Color(.6f,.64f,.64f,.65f);b.colors=c;
            Label(r,caption,18,new Vector2(.04f,.05f),new Vector2(.96f,.95f),Color.white);return b;
        }
    }
}
