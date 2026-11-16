// Keep a depth-tested label material synchronized with Unity's dynamic font atlas. MIT.
using UnityEngine;
namespace NPLAudio.SpatialFabric.Example
{
    [RequireComponent(typeof(TextMesh))]
    public sealed class RoomSign : MonoBehaviour
    {
        Material instance;
        void OnEnable(){Font.textureRebuilt+=Refresh;Apply();}
        void Refresh(Font font){if(font==GetComponent<TextMesh>().font)Apply();}
        void Apply()
        {
            var text=GetComponent<TextMesh>();var renderer=GetComponent<MeshRenderer>();
            if(instance==null)instance=new Material(renderer.sharedMaterial);
            instance.mainTexture=text.font.material.mainTexture;renderer.sharedMaterial=instance;
        }
        void OnDisable(){Font.textureRebuilt-=Refresh;}
        void OnDestroy(){if(instance!=null)Destroy(instance);}
    }
}
