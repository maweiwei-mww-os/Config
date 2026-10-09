using System.Collections;
using UnityEngine;

namespace DiceTown
{
    public sealed class TownWorld
    {
        public Camera Camera { get; private set; }
        public Transform Token { get; private set; }
        readonly Transform root;
        readonly Transform[] buildings = new Transform[3];
        readonly Vector3[] plots = { new Vector3(-1.8f,0,-.3f), new Vector3(1.7f,0,-.4f), new Vector3(0,0,2f) };
        readonly Color[] colors = { new Color(.95f,.38f,.30f), new Color(.20f,.68f,.62f), new Color(.98f,.70f,.21f) };
        readonly System.Collections.Generic.Dictionary<Color,Material> materials = new System.Collections.Generic.Dictionary<Color,Material>();
        readonly Transform[] coins = new Transform[12];
        public TownWorld(Transform parent)
        {
            root = new GameObject("Town world").transform; root.SetParent(parent);
            var background = new GameObject("Background camera").AddComponent<Camera>();background.transform.SetParent(root);
            background.depth=-10;background.cullingMask=0;background.clearFlags=CameraClearFlags.SolidColor;background.backgroundColor=new Color(.88f,.94f,.94f);
            Camera = new GameObject("Board camera").AddComponent<Camera>();
            Camera.transform.SetParent(root); Camera.transform.position = new Vector3(9,14,-12); Camera.transform.LookAt(Vector3.zero);
            Camera.orthographic = true; Camera.orthographicSize = 6.9f;
            Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(.88f,.94f,.94f);
            Camera.nearClipPlane=.1f; Camera.farClipPlane=100; Camera.gameObject.AddComponent<AudioListener>();
            var light = new GameObject("Sun").AddComponent<Light>(); light.transform.SetParent(root);
            light.type=LightType.Directional; light.intensity=1.2f; light.transform.rotation=Quaternion.Euler(48,-35,0); light.shadows=LightShadows.Soft;
            RenderSettings.ambientLight=new Color(.66f,.71f,.75f); RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            Shape(PrimitiveType.Cube,"Floating island",new Vector3(0,-.48f,0),new Vector3(10.4f,.72f,10.4f),new Color(.20f,.49f,.48f));
            Shape(PrimitiveType.Cube,"Board rim",new Vector3(0,-.08f,0),new Vector3(10.2f,.14f,10.2f),Color.white);
            Shape(PrimitiveType.Cube,"Town green",new Vector3(0,.01f,0),new Vector3(6.5f,.14f,6.5f),new Color(.58f,.78f,.64f));
            Shape(PrimitiveType.Cube,"Main street",new Vector3(0,.10f,-1.3f),new Vector3(6.3f,.02f,.65f),new Color(.83f,.86f,.81f));
            Shape(PrimitiveType.Cube,"Side street",new Vector3(0,.11f,.2f),new Vector3(.65f,.02f,5.8f),new Color(.83f,.86f,.81f));
            for(int i=0;i<24;i++)
            {
                Vector3 p=Position(i); bool special=i%6==0;
                Color color = i==5||i==17 ? new Color(.39f,.72f,.87f) : i==11||i==23 ? new Color(.96f,.68f,.35f) : special ? new Color(.24f,.62f,.55f) : Color.white;
                Shape(PrimitiveType.Cube,"Tile "+i,p-new Vector3(0,.20f,0),new Vector3(1.21f,.22f,1.21f),color);
                if(!special) Shape(PrimitiveType.Cube,"Property band",p+new Vector3(0,-.07f,.43f),new Vector3(1.16f,.035f,.24f),colors[(i/2)%3]);
                var marker=Shape(i%6==0?PrimitiveType.Cylinder:PrimitiveType.Cube,"Tile marker",p+new Vector3(0,-.015f,0),new Vector3(.25f,.035f,.25f),special?new Color(1,.89f,.49f):new Color(.30f,.47f,.49f));
                if(i==0) { marker.localScale=new Vector3(.5f,.045f,.5f); }
            }
            for(int i=0;i<10;i++)
            {
                float x=i%2==0?-2.7f:2.7f;float z=-2.6f+(i/2)*1.2f;
                Shape(PrimitiveType.Cylinder,"Tree trunk",new Vector3(x,.27f,z),new Vector3(.11f,.18f,.11f),new Color(.40f,.32f,.24f));
                Shape(PrimitiveType.Sphere,"Tree crown",new Vector3(x,.65f,z),new Vector3(.52f,.65f,.52f),i%3==0?new Color(.36f,.65f,.42f):new Color(.22f,.53f,.39f));
            }
            Token=new GameObject("Player token").transform;Token.SetParent(root);
            LocalShape(PrimitiveType.Cylinder,"Gold base",Token,new Vector3(0,.06f,0),new Vector3(.55f,.075f,.55f),new Color(1,.75f,.20f));
            LocalShape(PrimitiveType.Capsule,"Coral coat",Token,new Vector3(0,.35f,0),new Vector3(.34f,.28f,.34f),new Color(.97f,.31f,.24f));
            LocalShape(PrimitiveType.Sphere,"Head",Token,new Vector3(0,.73f,0),new Vector3(.32f,.32f,.32f),new Color(1,.83f,.64f));
            LocalShape(PrimitiveType.Cylinder,"Hat brim",Token,new Vector3(0,.87f,0),new Vector3(.49f,.035f,.49f),new Color(.12f,.23f,.26f));
            LocalShape(PrimitiveType.Cylinder,"Top hat",Token,new Vector3(0,1.02f,0),new Vector3(.32f,.14f,.32f),new Color(.12f,.23f,.26f));
            for(int i=0;i<coins.Length;i++) { coins[i]=Shape(PrimitiveType.Cylinder,"Reward coin",Vector3.zero,new Vector3(.24f,.035f,.24f),new Color(1,.71f,.14f));coins[i].gameObject.SetActive(false); }
        }
        public static Vector3 Position(int i)
        {
            i=(i%24+24)%24;float x,z;
            if(i<6){x=-3+i;z=-3;}else if(i<12){x=3;z=-3+i-6;}else if(i<18){x=3-(i-12);z=3;}else{x=-3;z=3-(i-18);}
            return new Vector3(x*1.4f,.3f,z*1.4f);
        }
        Transform Shape(PrimitiveType type,string name,Vector3 position,Vector3 scale,Color color)
        {return LocalShape(type,name,root,position,scale,color);}
        Transform LocalShape(PrimitiveType type,string name,Transform parent,Vector3 position,Vector3 scale,Color color)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
            Object.Destroy(go.GetComponent<Collider>());
            if(!materials.TryGetValue(color,out var material)){material=new Material(Resources.Load<Material>("TownSurface"));material.color=color;materials.Add(color,material);}
            go.GetComponent<Renderer>().sharedMaterial=material;return go.transform;
        }
        public void RefreshBuildings(int[] levels)
        {
            for(int b=0;b<3;b++)
            {
                if(buildings[b]!=null)Object.Destroy(buildings[b].gameObject);
                var group=new GameObject("Building "+b).transform;group.SetParent(root);group.position=plots[b];buildings[b]=group;
                LocalShape(PrimitiveType.Cube,"Plot",group,new Vector3(0,.16f,0),new Vector3(1.5f,.15f,1.6f),new Color(.92f,.92f,.84f));
                int floors=Mathf.Max(1,levels[b]);float height=.42f+floors*.28f;
                Color body=levels[b]==0?new Color(.77f,.80f,.77f):colors[b];
                LocalShape(PrimitiveType.Cube,"Facade",group,new Vector3(0,.24f+height/2,0),new Vector3(1.15f,height,1.1f),body);
                LocalShape(PrimitiveType.Cube,"Roof",group,new Vector3(0,.30f+height,0),new Vector3(1.3f,.18f,1.24f),new Color(.20f,.30f,.35f));
                for(int floor=0;floor<floors;floor++)for(int w=0;w<2;w++)
                    LocalShape(PrimitiveType.Cube,"Window",group,new Vector3(-.3f+w*.6f,.48f+floor*.28f,-.565f),new Vector3(.20f,.16f,.025f),new Color(1,.96f,.72f));
                LocalShape(PrimitiveType.Cube,"Door",group,new Vector3(0,.42f,-.57f),new Vector3(.25f,.4f,.035f),new Color(.21f,.33f,.35f));
                if(levels[b]>=3)LocalShape(PrimitiveType.Cylinder,"Roof detail",group,new Vector3(.32f,height+.58f,.2f),new Vector3(.3f,.18f,.3f),colors[b]);
            }
        }
        public IEnumerator Move(PendingTurn turn)
        {
            int steps=turn.Die1+turn.Die2;
            for(int i=0;i<steps;i++)
            {
                Vector3 from=Position(turn.Start+i),to=Position(turn.Start+i+1);
                for(float t=0;t<1;t+=Time.deltaTime/ .14f)
                {Token.position=Vector3.Lerp(from,to,t)+Vector3.up*Mathf.Sin(t*Mathf.PI)*.48f;yield return null;}
                Token.position=to;
            }
        }
        public IEnumerator Burst()
        {
            Vector3 origin=Token.position+Vector3.up*.7f;
            for(int i=0;i<coins.Length;i++)coins[i].gameObject.SetActive(true);
            for(float t=0;t<1;t+=Time.deltaTime/ .65f)
            {
                for(int i=0;i<coins.Length;i++)
                {float a=i*Mathf.PI*2/coins.Length; coins[i].position=origin+new Vector3(Mathf.Cos(a)*t,Mathf.Sin(t*Mathf.PI)*1.5f,Mathf.Sin(a)*t);coins[i].rotation=Quaternion.Euler(t*720,i*30,0);coins[i].localScale=Vector3.Lerp(new Vector3(.24f,.035f,.24f),Vector3.zero,t*t);}
                yield return null;
            }
            foreach(var coin in coins)coin.gameObject.SetActive(false);
        }
        public void Dispose(){foreach(var m in materials.Values)Object.Destroy(m);}
    }
}
