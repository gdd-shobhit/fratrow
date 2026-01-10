using UnityEngine;

namespace Unity.FPSSample_2
{
    public class EnterShooting : StateMachineBehaviour
    {
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsShooting", true);
        }
    }
}
