using UnityEngine;

namespace Unity.FPSSample_2
{
    public class EnterInAir : StateMachineBehaviour
    {
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool("IsInAir", true);
        }
    }
}
