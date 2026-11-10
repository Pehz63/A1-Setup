using UnityEngine;

/// <summary>
/// Calls BeginListening/StopListening on the talking cube when the right index trigger is pressed/released.
/// Stand-in for the Controller Buttons Mapper building block. Remove this component if the mapper is used.
/// </summary>
public class A3TriggerInput : MonoBehaviour
{
    [SerializeField] private A3TalkingCube talkingCube; // defaults to the A3TalkingCube on this GameObject

    [SerializeField] private OVRInput.Button button = OVRInput.Button.PrimaryIndexTrigger;
    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;

    void Awake()
    {
        if (talkingCube == null)
            talkingCube = GetComponent<A3TalkingCube>();
    }

    void Update()
    {
        if (talkingCube == null) return;

        if (OVRInput.GetDown(button, controller))
            talkingCube.BeginListening();

        if (OVRInput.GetUp(button, controller))
            talkingCube.StopListening();
    }
}
