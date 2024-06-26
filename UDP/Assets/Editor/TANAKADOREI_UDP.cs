using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Policy;
using System.Text;
using log4net.DateFormatter;
using PlasticPipe.PlasticProtocol.Messages;
using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace TANAKADOREI.UnityEditor.UDP
{
	public class TANAKADOREI_UDP_ViewerWindow : EditorWindow
	{
		[MenuItem("TANAKADOREI/(UDP) Unity development progress")]
		public static void ShowWindow()
		{
			GetWindow(typeof(TANAKADOREI_UDP_ViewerWindow), false, "UDP");
		}

		static void SetBackGroundColor(EditorWindow window, Color color)
		{
			var originalColor = GUI.backgroundColor;
			GUI.backgroundColor = color;
			GUI.Box(new Rect(0, 0, window.position.width, window.position.height), GUIContent.none);
			GUI.backgroundColor = originalColor;
		}

		static Rect RectAdd(in Rect sample, int add_value)
		{
			var new_rect = new Rect(sample.x, sample.y, sample.width, sample.height);
			new_rect.xMax += add_value;
			new_rect.xMin -= add_value;
			new_rect.yMax += add_value;
			new_rect.yMin -= add_value;
			return new_rect;
		}

		static void DrawProgress(Color theme, string title, string label, int frame_thickness, float t)
		{
			EditorGUILayout.LabelField($"{title}, [ {(int)(t * 100)}% ] : [{label}]", EditorStyles.boldLabel);

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
		bool cache_checker_select_mode;
		long m_last_on_gui_update_tick = DateTime.UtcNow.Ticks;
		int[] m_edit_start_time = new int[7]
		{
			DateTime.Now.Year,
			DateTime.Now.Month,
			DateTime.Now.Day,
			DateTime.Now.Hour,
			DateTime.Now.Minute,
			DateTime.Now.Second,
			0,// not utc
		};
		int[] m_edit_end_time = new int[7]
		{
			DateTime.Now.Year,
			DateTime.Now.Month,
			DateTime.Now.Day,
			DateTime.Now.Hour,
			DateTime.Now.Minute,
			DateTime.Now.Second,
			0,// not utc
		};
		bool m_edit_time = false;

		static void DrawSingleDateTime(int[] mem)
		{
			EditorGUILayout.LabelField("UTC", EditorStyles.boldLabel, GUILayout.Width(30));
			mem[6] = EditorGUILayout.Toggle(mem[6] == 0 ? false : true, GUILayout.Width(10)) ? 1 : 0;

			mem[0] = EditorGUILayout.IntField(mem[0], GUILayout.Width(40));
			EditorGUILayout.LabelField("/", EditorStyles.boldLabel, GUILayout.Width(10));
			mem[1] = EditorGUILayout.IntField(mem[1], GUILayout.Width(20));
			EditorGUILayout.LabelField("/", EditorStyles.boldLabel, GUILayout.Width(10));
			mem[2] = EditorGUILayout.IntField(mem[2], GUILayout.Width(20));
			EditorGUILayout.LabelField("-", EditorStyles.boldLabel, GUILayout.Width(10));
			mem[3] = EditorGUILayout.IntField(mem[3], GUILayout.Width(20));
			EditorGUILayout.LabelField(":", EditorStyles.boldLabel, GUILayout.Width(10));
			mem[4] = EditorGUILayout.IntField(mem[4], GUILayout.Width(20));
			EditorGUILayout.LabelField(":", EditorStyles.boldLabel, GUILayout.Width(10));
			mem[5] = EditorGUILayout.IntField(mem[5], GUILayout.Width(20));
		}

		static void DrawStartEndDateTime(int[] s_mem, int[] e_mem)
		{
			EditorGUILayout.BeginHorizontal();
			DrawSingleDateTime(s_mem);
			EditorGUILayout.LabelField("~", EditorStyles.boldLabel, GUILayout.Width(10));
			DrawSingleDateTime(e_mem);
			EditorGUILayout.EndHorizontal();
		}

		static bool TryApplyDateTime(in int[] start_mem, in int[] end_mem)
		{
			static DateTime ConvertIntArrToUTCDateTime(int[] mem)
			{
				var is_mem_utc_data = mem[6] == 0 ? DateTimeKind.Local : DateTimeKind.Utc;
				var data = new DateTime(mem[0], mem[1], mem[2], mem[3], mem[4], mem[5], is_mem_utc_data);
				if (is_mem_utc_data != DateTimeKind.Utc) data = data.ToUniversalTime();
				return data;
			}

			var start = ConvertIntArrToUTCDateTime(start_mem);
			var end = ConvertIntArrToUTCDateTime(end_mem);
			var now = DateTime.UtcNow;

			if (start < now && now < end)
			{
				RuntimeUDP_DATA.DATA.UTC_START = start.Ticks;
				RuntimeUDP_DATA.DATA.UTC_END = end.Ticks;
				return true;
			}
			else
			{
				Debug.LogError("Not applicable");
				return false;
			}
		}

		void OnGUI()
		{
			RuntimeUDP_DATA.Load_UDP_DATA();

			m_start_date_time_string ??= new((k) => DateTimeToString(k), (k1, k2) => k1 == k2);
			m_end_date_time_string ??= new((k) => DateTimeToString(k), (k1, k2) => k1 == k2);

			SetBackGroundColor(this, Color.black);
			const int PROGRESS_FRAME_THICKNESS = -2;

			var now_tick = DateTime.UtcNow.Ticks;

			var progress_time__t = (float)((double)(now_tick - RuntimeUDP_DATA.DATA.UTC_START) / (double)(RuntimeUDP_DATA.DATA.UTC_END - RuntimeUDP_DATA.DATA.UTC_START));
			var progress_me__t = RuntimeUDP_DATA.DATA.TODO_Progress_T;

			var progress_time__label = $"[{m_start_date_time_string.Get(RuntimeUDP_DATA.DATA.UTC_START)}] ~ [{m_start_date_time_string.Get(RuntimeUDP_DATA.DATA.UTC_END)}]";
			var progress_me__label = $"TODO : ({RuntimeUDP_DATA.DATA.RemainTODO_Count}) {progress_me__t}%";

			DrawProgress(Color.yellow, "Time", progress_time__label, PROGRESS_FRAME_THICKNESS, progress_time__t);
			DrawProgress(Color.cyan, "Me", progress_me__label, PROGRESS_FRAME_THICKNESS, progress_me__t);

			var is_time_expiration = !(RuntimeUDP_DATA.DATA.UTC_START < now_tick && now_tick < RuntimeUDP_DATA.DATA.UTC_END);

			if(is_time_expiration)
			{
				EditorGUILayout.LabelField("The time has expired. Please set the time again.");
			}

			EditorGUI.BeginDisabledGroup(is_time_expiration);
			RuntimeUDP_DATA.DATA.OnGUI(ref cache_checker_select_mode, ref m_scroll_vec, ref m_label, ref m_select_index);
			EditorGUI.EndDisabledGroup();

			// 일부러 모든 할일을 끝내놓고 시간대를 그나마 바꿀수있게 불편하게 맨아래에 뒀음
			if (m_edit_time)
			{
				DrawStartEndDateTime(m_edit_start_time, m_edit_end_time);
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("TimeApply"))
				{
					if (TryApplyDateTime(m_edit_start_time, m_edit_end_time))
					{
						m_edit_time = false;
					}
				}
				if (GUILayout.Button("CancelTimeSetting"))
				{
					m_edit_time = false;
				}
				EditorGUILayout.EndHorizontal();
			}
			else
			{
				if (GUILayout.Button("TimeSetting"))
				{
					m_edit_time = true;
				}
			}
		}

		void OnEnable()
		{
			EditorApplication.update += OnEditorUpdate;
			OnFocus();
		}

		void OnFocus()
		{
			RuntimeUDP_DATA.Load_UDP_DATA(true);
		}

		void OnLostFocus()
		{
			RuntimeUDP_DATA.Save_UDP_DATA();
		}

		void OnDisable()
		{
			EditorApplication.update -= OnEditorUpdate;
			OnLostFocus();
		}

		void OnEditorUpdate()
		{
			if (m_last_on_gui_update_tick != DateTime.UtcNow.Ticks)
			{
				m_last_on_gui_update_tick = DateTime.UtcNow.Ticks;
				Repaint();
			}
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
		public const string DEFAULT_CHECKER_NAME = nameof(TODO_Drawer.DefaultChecker);
		public ulong ID = 0;
		public string Label = "<NULL>";
		public string CheckerName = DEFAULT_CHECKER_NAME;

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


		public static void RefreshCheckers()
		{
			g_dict_checkers = new()
		{
			{ TODO.DEFAULT_CHECKER_NAME, DefaultChecker }
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
			if (GUILayout.Button("✔"))
			{
				TODO.CheckerDelegate checker = null;

				if (todo.CheckerName?.Length <= 0)
				{
					try
					{
						checker = g_dict_checkers[TODO.DEFAULT_CHECKER_NAME];
					}
					catch
					{
						Debug.LogError($"There is no checker '{TODO.DEFAULT_CHECKER_NAME}'. Try refreshing.");
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

		public static bool DefaultChecker(TODO todo)
		{
			return true;
		}
	}

	public static class UDP_Recorder
	{
		static string UDP_DATA_FILE_PATH => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UDP_HISTORY.line"));
		const char SEP_CHAR = '/';
		/// <summary>
		/// 완료한 TODO 기록
		/// </summary>
		public static void WriteLine(string msg)
		{
			File.AppendAllText(UDP_DATA_FILE_PATH,SEP_CHAR+msg,Encoding.UTF8);
		}
	}

	/// <summary>
	/// RuntimeUDP_DATA에 의해 관리되므로 해당 클래스를 경우해 사용
	/// </summary>
	public class UDP_DATA
	{
		[JsonProperty(nameof(UTC_START))]
		public long UTC_START = DateTime.MinValue.Ticks;
		[JsonProperty(nameof(UTC_END))]
		public long UTC_END = DateTime.MaxValue.Ticks;
		[JsonProperty("ID_COUNTER")]
		ulong m_id_counter = 0;
		[JsonProperty("TODO_LIST")]
		Dictionary<ulong, TODO> m_todo_list = new();

		[JsonIgnore]
		bool m_todo_list_updated = false;
		[JsonIgnore]
		public ulong RemainTODO_Count => (ulong)m_todo_list.Count;
		[JsonProperty("MEM001")]
		public ulong TODO_AddCapacity = 0;
		[JsonIgnore]
		public ulong TotalTODO_Count => m_id_counter;
		[JsonIgnore]
		public float TODO_Progress_T=>TODO_AddCapacity == 0 ? 1 : (1f - (float)((double)RemainTODO_Count / (double)TODO_AddCapacity));

		public ulong NewTODO(string label)
		{
			var id = ++m_id_counter;
			m_todo_list.Add(id, new(id, label));
			m_todo_list_updated = true;
			TODO_AddCapacity = RemainTODO_Count;
			return id;
		}

		public void CompleteTODO(TODO todo)
		{
			if (!m_todo_list.Remove(todo.ID))
			{
				Debug.LogError("This is a TODO that has already been removed or does not exist.");
			}
			else
			{
				if (m_todo_list.Count == 0)
				{
					TODO_AddCapacity = 0;
				}
				m_todo_list_updated = true;

				UDP_Recorder.WriteLine($"{UTC_START};{UTC_END};{DateTime.UtcNow.Ticks};{RemainTODO_Count};{TODO_AddCapacity};{TODO_Progress_T};{todo.ID};{todo.Label};{todo.CheckerName}");
			}
		}

		public void OnGUI(ref bool cache_checker_select_mode, ref Vector2 scroll_vec, ref string cache_todo_label, ref int cache_select_index)
		{
			EditorGUILayout.BeginHorizontal();
			{
				EditorGUILayout.LabelField("TODO");
				cache_todo_label = EditorGUILayout.TextField(cache_todo_label);
				if (GUILayout.Button("New TODO"))
				{
					var id = NewTODO(cache_todo_label).ToString();
					cache_todo_label = "";
					GUIUtility.systemCopyBuffer = id;
					Debug.Log($"TODO ID Copied. `{id}`");
				}

				cache_checker_select_mode = GUILayout.Toggle(cache_checker_select_mode, "SelectChecker");
			}
			EditorGUILayout.EndHorizontal();

			scroll_vec = EditorGUILayout.BeginScrollView(scroll_vec);
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
						if (cache_checker_select_mode)
						{
							cache_select_index = EditorGUILayout.Popup(cache_select_index, TODO_Drawer.CheckerNames);
							var selected_checker_name = TODO_Drawer.CheckerNames[cache_select_index];
							if (todo.CheckerName != selected_checker_name)
							{
								m_todo_list_updated = true;
								todo.CheckerName = selected_checker_name;
								cache_checker_select_mode = false;
							}
							cache_select_index = 0;
						}
						else
						{
							if (todo.CheckerName?.Length <= 0)
							{
								todo.CheckerName = TODO.DEFAULT_CHECKER_NAME;
							}
							todo.CheckerName = EditorGUILayout.TextField(todo.CheckerName);
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

		public static void Load_UDP_DATA(bool force = false)
		{
			if (!force && m_udp_data != null) return;
			TODO_Drawer.RefreshCheckers();
			try
			{
				m_udp_data = JsonConvert.DeserializeObject<UDP_DATA>(File.ReadAllText(UDP_DATA_FILE_PATH));
			}
			catch
			{
				m_udp_data = new UDP_DATA();
			}
		}

		public static void Save_UDP_DATA()
		{
			try
			{
				File.WriteAllText(UDP_DATA_FILE_PATH, JsonConvert.SerializeObject(m_udp_data, Formatting.Indented));
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
