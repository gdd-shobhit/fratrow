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
		public bool phoneToggle;
		public bool[] hotbarSlots = new bool[8];

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

		public void OnPhone(InputValue value)
		{
			PhoneInput(value.isPressed);
		}

		public void OnHotbarSlot1(InputValue value) => HotbarSlotInput(0, value.isPressed);
		public void OnHotbarSlot2(InputValue value) => HotbarSlotInput(1, value.isPressed);
		public void OnHotbarSlot3(InputValue value) => HotbarSlotInput(2, value.isPressed);
		public void OnHotbarSlot4(InputValue value) => HotbarSlotInput(3, value.isPressed);
		public void OnHotbarSlot5(InputValue value) => HotbarSlotInput(4, value.isPressed);
		public void OnHotbarSlot6(InputValue value) => HotbarSlotInput(5, value.isPressed);
		public void OnHotbarSlot7(InputValue value) => HotbarSlotInput(6, value.isPressed);
		public void OnHotbarSlot8(InputValue value) => HotbarSlotInput(7, value.isPressed);
#endif
        private void InteractInput(bool isPressed)
        {
            interact = isPressed;
        }

		private void InventoryInput(bool isPressed)
		{
			inventoryToggle = isPressed;
		}

		private void PhoneInput(bool isPressed)
		{
			phoneToggle = isPressed;
		}

		private void HotbarSlotInput(int slotIndex, bool isPressed)
		{
			if (!isPressed || slotIndex < 0 || slotIndex >= hotbarSlots.Length) {
				return;
			}

			hotbarSlots[slotIndex] = true;
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