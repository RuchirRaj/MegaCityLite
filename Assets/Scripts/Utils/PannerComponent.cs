using UnityEngine;
using UnityEngine.Experimental.Rendering.HDPipeline;

public class PannerComponent : MonoBehaviour {
    public HDAdditionalLightData   target;
    public Vector2                 velocity;

    void Update() {
        var dt = Time.deltaTime;
        var pos = target.transform.position;
        pos.x += velocity.x * dt;
        pos.z += velocity.y * dt;
        target.transform.position = pos;
    }
}
