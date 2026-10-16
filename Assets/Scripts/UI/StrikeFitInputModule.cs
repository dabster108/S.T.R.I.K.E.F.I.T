// StrikeFitInputModule.cs
// Purpose: OVRInputModule with a restricted click. The stock module clicks on OVRInput.GetDown(joyPadClickButton) with the
//          default "Active" controller, which is whichever controller registered a press most recently: the LEFT trigger (or
//          left X, for A) clicked while the laser was on the right hand. This subclass clicks on the pointing hand's trigger
//          only (right by default), as a debounced button-down through ControllerInput. It also gives a haptic tick when the
//          ray moves onto a button and on a click. With ControlSettings.mappingEnabled off it behaves exactly like the stock module.
// Dependencies: OVRInputModule (Meta XR SDK), ControllerInput, ControllerMapping, ControlSettings
// Attach to: EventSystem in every scene (replaces OVRInputModule; same serialized fields)

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class StrikeFitInputModule : OVRInputModule
{
    Selectable hovered;
    bool pressedThisFrame;

    /// <summary>The interactable button the ray is on (null when none).</summary>
    public Selectable Hovered => hovered;

    protected override PointerEventData.FramePressState GetGazeButtonState()
    {
        if (!ControllerMapping.Active) return base.GetGazeButtonState();

        var hand = ControllerMapping.PointHand;
        bool pressed = ControllerInput.Down(Pad.Trigger, hand);
        bool released = ControllerInput.Up(Pad.Trigger, hand);

        // Optional second laser: the other hand's trigger moves the laser to that hand instead of clicking.
        if (ControlSettings.Current.leftLaser && ControllerMapping.LaserWanted)
        {
            var other = hand == OVRInput.Controller.LTouch ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            if (ControllerInput.Down(Pad.Trigger, other))
            {
                ControllerMapping.SwitchHand(other);
                pressed = false;
            }
        }

#if UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetKeyDown(gazeClickKey);
        released |= Input.GetKeyUp(gazeClickKey);
#endif
        pressedThisFrame = pressed;

        if (pressed && released) return PointerEventData.FramePressState.PressedAndReleased;
        if (pressed) return PointerEventData.FramePressState.Pressed;
        if (released) return PointerEventData.FramePressState.Released;
        return PointerEventData.FramePressState.NotChanged;
    }

    public override void Process()
    {
        pressedThisFrame = false;
        base.Process();
        if (!ControllerMapping.Active) return;

        // GetPointerData returns true only when it CREATES the entry, but always fills `data` when one exists.
        GameObject over = null;
        GetPointerData(kMouseLeftId, out OVRPointerEventData data, false);
        if (data != null) over = data.pointerCurrentRaycast.gameObject;
        Selectable sel = over != null ? over.GetComponentInParent<Selectable>() : null;
        if (sel != null && !sel.IsInteractable()) sel = null;
        if (sel != hovered)
        {
            hovered = sel;
            if (sel != null) ControllerMapping.Tick(false);
        }
        if (pressedThisFrame && hovered != null) ControllerMapping.Tick(true);
    }
}
