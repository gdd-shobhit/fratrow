using UnityEngine;

namespace Unity.FPSSample_2
{
    public class ExitReload : StateMachineBehaviour
    {
        override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsReloading", false);
        }
    }
}