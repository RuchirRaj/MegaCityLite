using UnityEngine;
using Unity.Entities;

namespace Unity.Audio.Megacity
{
    public class TrafficAudioSettings : MonoBehaviour
    {
        public AudioClip[] audioClips;
        public AudioClip[] vehicleLowIntensities;
        public AudioClip[] vehicleHighIntensities;

        public float Falloff = 2.1f;

        [Range(0, 1)]
        public float Volume = 0.5f;

        public FlyByParameters flyByParameters;
        public TrafficAudioParameters trafficAudioParameters;

        TrafficAudioFieldSystem m_TrafficFieldSystem;

        FlyBySystem m_FlyBySystem;

        SoundCollection m_FlyBySounds;

        void OnEnable()
        {
            m_TrafficFieldSystem = World.Active.GetOrCreateManager<TrafficAudioFieldSystem>();
            m_FlyBySystem = World.Active.GetOrCreateManager<FlyBySystem>();

            m_FlyBySounds = m_FlyBySystem.CreateCollection();

            foreach (var clip in audioClips)
                m_TrafficFieldSystem.AddDistributedSamplePlayback(clip);

            foreach (var clip in vehicleLowIntensities)
            {
                if (clip != null)
                    m_FlyBySystem.AddLowFlyBySound(m_FlyBySounds, clip);
            }

            foreach (var clip in vehicleHighIntensities)
            {
                if (clip != null)
                    m_FlyBySystem.AddHighFlyBySound(m_FlyBySounds, clip);
            }

            m_TrafficFieldSystem.SetFlyBySoundGroup(m_FlyBySounds);
        }

        void Update()
        {
            m_TrafficFieldSystem.SetParameters(trafficAudioParameters);
            m_FlyBySystem.SetParameters(flyByParameters);
        }

        void OnDisable()
        {
            if (World.Active != null && World.Active.IsCreated)
            {
                m_TrafficFieldSystem.DetachFromAllVehicles();
                m_TrafficFieldSystem.ClearSamplePlaybacks();
                m_FlyBySystem.ClearCollections();
            } 
        }
    }
}
