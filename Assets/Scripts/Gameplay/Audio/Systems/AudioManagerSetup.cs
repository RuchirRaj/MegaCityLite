using Cinemachine;
using Unity.Entities;
using UnityEngine;

namespace Unity.Audio.Megacity
{
    [UpdateBefore(typeof(AudioManagerSystem))]
    public class AudioManagerSetup : ComponentSystem
    {
        public string CameraGameObjectName = "PlayerCam";

        AudioManagerSystem m_AudioManager;

        GameObject m_MainCameraObject;
        CinemachineBrain m_CinemachineBrain;

        bool m_FoundActualPlayer;

        protected override void OnCreateManager()
        {
            m_AudioManager = World.GetOrCreateManager<AudioManagerSystem>();

            UpdateLinks();
        }

        void UpdateLinks()
        {
            if (m_CinemachineBrain == null)
            {
                m_CinemachineBrain = GetCameraObject()?.GetComponent<CinemachineBrain>();                
            }
        }

        protected override void OnUpdate()
        {
            UpdateLinks();

            m_AudioManager.ListenerTransform = GetCameraObject()?.transform;

            if (!m_FoundActualPlayer)
            {
                var transform = m_CinemachineBrain?.ActiveVirtualCamera?.LookAt?.parent;

                if (transform != null)
                {

                    if (!transform.gameObject.CompareTag("Player"))
                    {
                        transform = transform.GetChild(0);
                    }
                }

                m_FoundActualPlayer = transform != null;

                m_AudioManager.PlayerTransform = transform ?? m_AudioManager.ListenerTransform;

            }

            m_AudioManager.PlayerTransform = m_AudioManager.PlayerTransform ?? m_AudioManager.ListenerTransform;
        }

        GameObject GetCameraObject()
        {
            if (m_MainCameraObject == null)
            {
                m_MainCameraObject = GameObject.Find(CameraGameObjectName) ?? Camera.main?.gameObject;
            }
            return m_MainCameraObject;
        }
    }
}
