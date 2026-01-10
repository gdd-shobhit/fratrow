using UnityEngine;

namespace Unity.FPSSample_2
{
    public class ExitLanding : StateMachineBehaviour
    {
        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsInAir", false);
        }
    }
}
