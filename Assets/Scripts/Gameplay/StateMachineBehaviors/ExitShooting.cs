using UnityEngine;

namespace Unity.FPSSample_2
{
    public class ExitShooting : StateMachineBehaviour
    {
        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsShooting", false);
        }
    }
}
