using UnityEngine;
using Unity.Entities;

namespace Unity.Audio.Megacity
{
    class FlyCam : MonoBehaviour
    {
        public float lookSpeed = 5.0f;
        public float moveSpeed = 3.0f;

        public float rotationX = 0.0f;
        public float rotationY = 0.0f;

        void Start()
        {
            World.Active.GetOrCreateManager<AudioManagerSystem>().SetActive(true);
        }

        void Update()
        {
            if (Input.GetMouseButton(0))
            {
                rotationX += Input.GetAxis("Mouse X") * lookSpeed;
                rotationY += Input.GetAxis("Mouse Y") * lookSpeed;
                rotationY = Mathf.Clamp(rotationY, -90.0f, 90.0f);
            }

            transform.localRotation = Quaternion.AngleAxis(rotationX, Vector3.up);
            transform.localRotation *= Quaternion.AngleAxis(rotationY, Vector3.left);

            transform.position += transform.forward * (Input.GetKey("w") ? moveSpeed : Input.GetKey("s") ? -moveSpeed : 0.0f);
            transform.position += transform.right * (Input.GetKey("a") ? -moveSpeed : Input.GetKey("d") ? moveSpeed : 0.0f);
            transform.position += transform.up * 3 * moveSpeed * Input.GetAxis("Mouse ScrollWheel");
        }
    }
}
