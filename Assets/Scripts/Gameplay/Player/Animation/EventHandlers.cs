using System;
using UnityEngine;

namespace Unity.FPSSample_2
{
    public class EventHandler : MonoBehaviour
    {
        public float minFootstepInterval = 0.2f;
        public SoundDef footstep;
        public SoundDef reloadSFX;

        [NonSerialized] public bool onFootDown;
        [NonSerialized] public bool onLand;
        [NonSerialized] public bool onDoubleJump;
        [NonSerialized] public bool onJumpStart;

        public void OnCharEvent(AnimationEvent e)
        {
            onFootDown = true;
        }

        public void OnLand(AnimationEvent e)
        {
            onFootDown = true;
        }

        public void ReloadAnimationSFXTrigger(AnimationEvent e)
        {
            if (reloadSFX != null)
            {
                GameManager.Instance.SoundSystem.CreateEmitter(reloadSFX, transform);
            }
        }

        void Update()
        {
            if (onFootDown)
            {
                GameManager.Instance.SoundSystem.CreateEmitter(footstep, transform);
                onFootDown = false;
            }
        }
    }
}