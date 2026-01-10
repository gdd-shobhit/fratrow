using UnityEngine;

namespace Unity.FPSSample_2
{
    public class ExitHit : StateMachineBehaviour
    {
        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsHit", false);
        }
    }
}
