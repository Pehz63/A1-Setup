using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Oculus.Interaction.Input;

public class A2Interaction : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [SerializeField]
    private Hand leftHand; // TO DO: assign OVRLeftHandDataSource to this in the inspector
    [SerializeField]
    private Hand rightHand; // TO DO: assign OVRRightHandDataSource to this in the inspector

    [SerializeField]
    private TextMeshPro LeftHandPosText; // TO DO: assign a TextMeshPro object in the inspector

    [SerializeField]
    private TextMeshPro RightHandPosText; // TO DO: assign a TextMeshPro object in the inspector

    [SerializeField]
    private TextMeshPro StateText; // TO DO: assign a TextMeshPro object in the inspector

    [SerializeField]
    private float closeDistance = 0.05f; // meters between the two joints that counts as "close"

    private string gestureMessage = ""; // set by ThumbsUp/NoThumbsUp, shown under the distance by Update

    private Pose leftHandPose;
    private Pose rightHandPose;
    private HandJointId handJointId = HandJointId.HandIndex3; // Note: you can change this to any bone you want, such as HandThumbTip, HandMiddleTip, etc.

    void Update()
    {
        leftHand.GetJointPose(handJointId, out leftHandPose);
        // 1. display the position and rotation of the left hand joint in the LeftHandPosText
        LeftHandPosText.text = $"Left {handJointId}\nPos: {leftHandPose.position.ToString("F3")}\nRot: {leftHandPose.rotation.eulerAngles.ToString("F0")}";

        rightHand.GetJointPose(handJointId, out rightHandPose);
        // 2. display the position and rotation of the right hand joint in the RightHandPosText
        RightHandPosText.text = $"Right {handJointId}\nPos: {rightHandPose.position.ToString("F3")}\nRot: {rightHandPose.rotation.eulerAngles.ToString("F0")}";

        // 3. if two hand poses are close to each other, display a message in the StateText
        float distance = Vector3.Distance(leftHandPose.position, rightHandPose.position);
        if (distance < closeDistance)
        {
            StateText.text = $"Fingers are touching!\nDistance: {distance:F3} m";
        }
        else
        {
            StateText.text = $"Distance: {distance:F3} m";
        }

        if (gestureMessage != "")
        {
            StateText.text += "\n" + gestureMessage;
        }
    }

    public void ThumbsUp()
    {
        // 4. when the user gives a thumbs up gesture, display a message in the StateText
        gestureMessage = "Thumbs Up!";
    }

    public void NoThumbsUp()
    {
        // 5. when the user gives a no thumbs up gesture, display a message in the StateText
        gestureMessage = "Thumbs Down";
    }
}
