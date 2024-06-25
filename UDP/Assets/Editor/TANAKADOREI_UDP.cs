using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Policy;
using PlasticPipe.PlasticProtocol.Messages;
using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace TANAKADOREI.UnityEditor.UDP
{
	public class TANAKADOREI_UDP_ViewerWindow : EditorWindow
	{
		[MenuItem("TANAKADOREI/(UDP) Unity development progress : Viewer")]
		public static void ShowWindow()
		{
			GetWindow<TANAKADOREI_UDP_ViewerWindow>("UDP Viewer");
		}

		static void SetBackGroundColor(EditorWindow window, Color color)
		{
			SetBackGroundColor2(new Rect(0, 0, window.position.width, window.position.height),color);
		}

		static void SetBackGroundColor2(Rect rect, Color color)
		{
			var originalColor = GUI.backgroundColor;
			GUI.backgroundColor = color;
			GUI.Box(rect, GUIContent.none);
			GUI.backgroundColor = originalColor;
		}

		static Rect RectAdd(in Rect rect, int add_value)
		{
			var new_rect = new Rect(rect);
			new_rect.xMax += add_value;
			new_rect.xMin -= add_value;
			new_rect.yMax += add_value;
			new_rect.yMin -= add_value;
			return new_rect;
		}

		static void DrawProgress(Color theme, string title, string label, int frame_thickness, float t)
		{
			EditorGUILayout.LabelField($"{title}, [ {t * 100}% ] : [{label}]", EditorStyles.boldLabel);

			frame_thickness = frame_thickness < 0 ? frame_thickness : -frame_thickness;
			var time_progress_frame_rect = EditorGUILayout.GetControlRect(false, 24);
			var time_progress_back_rect = RectAdd(time_progress_frame_rect, frame_thickness);
			var time_progress__bar_rect = RectAdd(time_progress_back_rect, frame_thickness);

			time_progress__bar_rect.xMax = Mathf.Lerp(time_progress__bar_rect.xMin, time_progress__bar_rect.xMax, t);

			EditorGUI.DrawRect(time_progress_frame_rect, Color.white);
			EditorGUI.DrawRect(time_progress_back_rect, Color.black);
			EditorGUI.DrawRect(time_progress__bar_rect, theme);
		}

		static string DateTimeToString(long utc_tick)
		{
			return new DateTime(utc_tick, DateTimeKind.Utc).ToString("yyyy/MM/dd(ddd)-HH:mm:ss");
		}

		static ulong Lerp(ulong start, ulong end, float t)
		{
			if (t < 0.0)
			{
				t = 0;
			}
			else if (t > 1.0)
			{
				t = 1;
			}
			ulong result = (ulong)((1 - t) * start + t * end);
			return result;
		}

		CondLazy<long, string> m_start_date_time_string;
		CondLazy<long, string> m_end_date_time_string;
		Vector2 m_scroll_vec = Vector2.zero;
		int m_select_index;
		string m_label;

		void OnGUI()
		{
			if (RuntimeUDP_DATA.DATA == null)
			{
				RuntimeUDP_DATA.Load_UDP_DATA();
				return;
			}

			m_start_date_time_string ??= new((k) => DateTimeToString(k), (k1, k2) => k1 == k2);
			m_end_date_time_string ??= new((k) => DateTimeToString(k), (k1, k2) => k1 == k2);

			SetBackGroundColor(this, Color.black);
			const int PROGRESS_FRAME_THICKNESS = -2;

			var now_time = DateTime.UtcNow.Ticks;

			var t = (now_time - RuntimeUDP_DATA.DATA.UTC_START) / (RuntimeUDP_DATA.DATA.UTC_END - RuntimeUDP_DATA.DATA.UTC_START);

			double t2 = (double)RuntimeUDP_DATA.DATA.TODO_Count / (double)RuntimeUDP_DATA.DATA.AllocatedCount;

			DrawProgress(Color.yellow, "Time", $"[{m_start_date_time_string.Get(RuntimeUDP_DATA.DATA.UTC_START)}] ~ [{m_start_date_time_string.Get(RuntimeUDP_DATA.DATA.UTC_END)}]", PROGRESS_FRAME_THICKNESS, t);

			DrawProgress(Color.cyan, "Me", $"TODO : ({RuntimeUDP_DATA.DATA.TODO_Count}) {t2}%", PROGRESS_FRAME_THICKNESS, 1f - (float)t2);

			RuntimeUDP_DATA.DATA.OnGUI(ref m_scroll_vec, ref m_label, ref m_select_index);
		}
	}

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	public class TODO_HasChecker : Attribute
	{
		public string CheckStaticMethodName;
		public string CheckerName;

		public TODO_HasChecker(string checkStaticMethodName, string checkerName = null)
		{
			CheckStaticMethodName = checkStaticMethodName;
			CheckerName = checkerName?.Length <= 0 ? checkStaticMethodName : checkerName;
		}
	}

	public class TODO
	{
		public ulong ID = 0;
		public string Label = "<NULL>";
		public string CheckerName = "";

		public delegate bool CheckerDelegate(TODO todo);

		public TODO(ulong id, string label)
		{
			ID = id;
			Label = label;
		}

		public override bool Equals(object obj)
		{
			return obj is TODO todo && todo.ID == ID;
		}

		public override int GetHashCode() => ID.GetHashCode();

		public override string ToString() => $"[#{ID}]:[{Label}]";
	}

	public class TODO_Drawer
	{
		private static Dictionary<string, TODO.CheckerDelegate> g_dict_checkers = null;
		private static string[] g_checker_names = null;

		public static string[] CheckerNames
		{
			get
			{
				if (g_dict_checkers?.Count <= 0)
				{
					RefreshCheckers();
				}
				return g_checker_names;
			}
		}

		public const string DEFAULT_CHECKER_NAME = nameof(DefaultChecker);

		public static void RefreshCheckers()
		{
			g_dict_checkers = new()
		{
			{ DEFAULT_CHECKER_NAME, DefaultChecker }
		};

			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				foreach (var type in asm.GetTypes())
				{
					var attr = type.GetCustomAttribute<TODO_HasChecker>();
					if (attr == null || attr.CheckStaticMethodName?.Length <= 0) continue;
					var checker = Delegate.CreateDelegate(typeof(TODO.CheckerDelegate), type.GetMethod(attr.CheckStaticMethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) as TODO.CheckerDelegate;

					if (attr != null && checker != null)
					{
						if (!g_dict_checkers.TryAdd(attr.CheckerName, checker))
						{
							Debug.LogError($"[ignored] : already exists todo checker, [{attr.CheckerName} : {attr.CheckStaticMethodName}]");
						}
					}
				}
			}

			g_checker_names = g_dict_checkers.Keys.ToArray();
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="udp_data"></param>
		/// <param name="todo"></param>
		/// <returns>updated</returns>
		public static void OnGUI_TODO_Tasks(UDP_DATA udp_data, TODO todo)
		{
			if (GUILayout.Button("-"))
			{
				udp_data.RemoveTODO(todo.ID);
				return;
			}

			if (GUILayout.Button("✔"))
			{
				TODO.CheckerDelegate checker = null;

				if (todo.CheckerName?.Length <= 0)
				{
					try
					{
						checker = g_dict_checkers[DEFAULT_CHECKER_NAME];
					}
					catch
					{
						Debug.LogError($"There is no checker '{DEFAULT_CHECKER_NAME}'. Try refreshing.");
					}
				}
				else
				{
					if (!g_dict_checkers.TryGetValue(todo.CheckerName, out checker))
					{
						Debug.LogError($"There is no checker '{todo.CheckerName}'. Try refreshing.");
					}
				}

				if (checker == null) return;

				if (checker(todo))
				{
					udp_data.CompleteTODO(todo);
					Debug.Log($"Task `{todo}` is completed. Congratulations!");
				}
				else
				{
					Debug.LogWarning($"TODO `{todo}` is not completed yet..");
				}
			}
		}

		private static bool DefaultChecker(TODO todo)
		{
			return true;
		}
	}

	/// <summary>
	/// RuntimeUDP_DATA에 의해 관리되므로 해당 클래스를 경우해 사용
	/// </summary>
	public class UDP_DATA
	{
		public long UTC_START = DateTime.MinValue.Ticks;
		public long UTC_END = DateTime.MaxValue.Ticks;
		ulong m_id_counter = 0;
		Dictionary<ulong, TODO> m_todo_list = new();

		[JsonIgnore]
		bool m_todo_list_updated = false;
		[JsonIgnore]
		public ulong TODO_Count => (ulong)m_todo_list.Count;
		public ulong AllocatedCount => m_id_counter;

		public ulong NewTODO(string label)
		{
			var id = ++m_id_counter;
			m_todo_list.Add(id, new(id, label));
			m_todo_list_updated = true;
			return id;
		}

		public void RemoveTODO(ulong id)
		{
			m_todo_list.Remove(id);
			m_todo_list_updated = true;
		}

		public void CompleteTODO(TODO todo)
		{
			if (!m_todo_list.Remove(todo.ID))
			{
				Debug.LogError("This is a TODO that has already been removed or does not exist.");
			}
			m_todo_list_updated = true;
		}

		public void OnGUI(ref Vector2 scroll_vec, ref string cache_todo_label, ref int cache_select_index)
		{
			EditorGUILayout.BeginHorizontal();
			{
				cache_todo_label = EditorGUILayout.TextField("TODO:", cache_todo_label);
				if (GUILayout.Button("New TODO"))
				{
					var id = NewTODO(cache_todo_label).ToString();
					GUIUtility.systemCopyBuffer = id;
					Debug.Log($"TODO ID Copied. `{id}`");
				}
			}
			EditorGUILayout.EndHorizontal();

			scroll_vec = EditorGUILayout.BeginScrollView(scroll_vec, GUILayout.MinHeight(24));
			using (var iter = m_todo_list.GetEnumerator())
			{
				while (iter.MoveNext())
				{
					var todo = iter.Current.Value;
					EditorGUILayout.BeginHorizontal();
					{
						var temp = EditorGUILayout.TextField($"#{todo.ID}", todo.Label);
						if (todo.Label != temp)
						{
							m_todo_list_updated = true;
							todo.Label = temp;
						}
						{
							cache_select_index = EditorGUILayout.Popup(cache_select_index, TODO_Drawer.CheckerNames);
							var selected_checker_name = TODO_Drawer.CheckerNames[cache_select_index];
							if (todo.CheckerName != selected_checker_name)
							{
								m_todo_list_updated = true;
								todo.CheckerName = selected_checker_name;
							}
						}

						TODO_Drawer.OnGUI_TODO_Tasks(this, todo);

						if (m_todo_list_updated) // 위 작업중에, 리스트가 수정 되었다면 이번 프레임 그리기 종료. iter때문에.
						{
							m_todo_list_updated = false;
							EditorGUILayout.EndHorizontal();
							EditorGUILayout.EndScrollView();
							RuntimeUDP_DATA.Save_UDP_DATA();
							return; // 리스트 변경됨, iter 주의
						}
					}
					EditorGUILayout.EndHorizontal();
				}
			}
			EditorGUILayout.EndScrollView();
		}
	}

	public static class RuntimeUDP_DATA
	{
		static string UDP_DATA_FILE_PATH => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UDP_DATA.json"));

		static UDP_DATA m_udp_data = null;

		public static UDP_DATA DATA => m_udp_data;

		public static void Load_UDP_DATA()
		{
			try
			{
				TODO_Drawer.RefreshCheckers();
				m_udp_data = JsonConvert.DeserializeObject<UDP_DATA>(File.ReadAllText(UDP_DATA_FILE_PATH));
			}
			catch
			{
			}
			m_udp_data ??= new UDP_DATA();
		}

		public static void Save_UDP_DATA()
		{
			try
			{
				File.WriteAllText(UDP_DATA_FILE_PATH, JsonConvert.SerializeObject(m_udp_data));
			}
			catch (Exception ex)
			{
				Debug.LogError($"save error : {ex}");
			}
		}
	}

	public class CondLazy<K, V>
	{
		public delegate bool Compare(K old, K target);

		K m_old_key;
		V m_old_value;
		Compare m_compare;
		Func<K, V> m_factory;
		bool req_update;

		public bool IsReqUpdate => req_update;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="factory"></param>
		/// <param name="compare"></param>
		public CondLazy(Func<K, V> factory, Compare compare = null)
		{
			m_compare = compare;
			m_factory = factory;
			req_update = true;
		}

		/// <summary>
		/// 이전 키로, 값을 업데이트 한다
		/// </summary>
		/// <returns></returns>
		public V Get()
		{
			if (req_update)
			{
				return m_old_value = m_factory(m_old_key);
			}
			return m_old_value;
		}

		/// <summary>
		/// 이전키와 현재키를 비교해 다르면 업데이트한다
		/// </summary>
		/// <param name="key"></param>
		/// <returns></returns>
		public V Get(K key)
		{
			if (!req_update && m_compare(m_old_key, key))
			{
				return m_old_value;
			}
			else
			{
				m_old_key = key;
				return m_old_value = m_factory(key);
			}
		}

		public void ReqUpdate()
		{
			req_update = true;
		}
	}
}
