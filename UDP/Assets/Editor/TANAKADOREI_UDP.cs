using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class TANAKADOREI_UDP : EditorWindow
{
	public static void ShowWindow()
	{
		GetWindow<TANAKADOREI_UDP>("UDP");
	}

	void OnGUI()
	{
		var time_progress_rect = EditorGUILayout.GetControlRect(false,24);
		var my_goal_progress_rect = EditorGUILayout.GetControlRect(false,24);

		EditorGUI.DrawRect()
	}
}
