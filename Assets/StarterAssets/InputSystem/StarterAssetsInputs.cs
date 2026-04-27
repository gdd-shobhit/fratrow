using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	public class StarterAssetsInputs : MonoBehaviour
	{
		[Header("Character Input Values")]
		public Vector2 move;
		public Vector2 look;
		public bool jump;
		public bool sprint;
        public bool drink;
		public bool interact;
		public bool inventoryToggle;

        [Header("Movement Settings")]
		public bool analogMovement;

		[Header("Mouse Cursor Settings")]
		public bool cursorLocked = true;
		public bool cursorInputForLook = true;

#if ENABLE_INPUT_SYSTEM
		public void OnMove(InputValue value)
		{
			MoveInput(value.Get<Vector2>());
		}

		public void OnLook(InputValue value)
		{
			if(cursorInputForLook)
			{
				LookInput(value.Get<Vector2>());
			}
		}

		public void OnJump(InputValue value)
		{
			JumpInput(value.isPressed);
		}

		public void OnSprint(InputValue value)
		{
			SprintInput(value.isPressed);
		}
        public void OnDrink(InputValue value)
        {
            DrinkInput(value.isPressed);
        }
        public void OnInteract(InputValue value)
        {
            InteractInput(value.isPressed);
        }

		public void OnInventory(InputValue value)
		{
			InventoryInput(value.isPressed);
		}
#endif
        private void InteractInput(bool isPressed)
        {
            interact = isPressed;
        }

		private void InventoryInput(bool isPressed)
		{
			inventoryToggle = isPressed;
		}
        public void MoveInput(Vector2 newMoveDirection)
		{
			move = newMoveDirection;
		} 

		public void LookInput(Vector2 newLookDirection)
		{
			look = newLookDirection;
		}

		public void JumpInput(bool newJumpState)
		{
			jump = newJumpState;
		}
        public void DrinkInput(bool newDrinkState)
        {
            drink = newDrinkState;
        }

        public void SprintInput(bool newSprintState)
		{
			sprint = newSprintState;
		}
		
		private void OnApplicationFocus(bool hasFocus)
		{
			ApplyCursorFromSettings();
		}

		/// <summary>Inventory open: free cursor for UI. Closed: return to FPS (locked, look enabled).</summary>
		public void SetInventoryUIMode(bool inventoryOpen)
		{
			cursorLocked = !inventoryOpen;
			cursorInputForLook = !inventoryOpen;
			LookInput(Vector2.zero);
			ApplyCursorFromSettings();
		}

		void ApplyCursorFromSettings()
		{
			Cursor.lockState = cursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
			Cursor.visible = !cursorLocked;
		}
	}
	
}