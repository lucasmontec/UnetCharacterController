using UnityEngine;
using System.Collections;

public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
	private static T m_Instance;
	private static object m_Lock = new object();
	private static bool m_ApplicationIsQuitting = false;
	
	public static T Instance
	{
		get
		{
			if (m_ApplicationIsQuitting)
				return null;
			
			lock(m_Lock)
			{
				if (m_Instance == null)
				{
					m_Instance = (T)FindObjectOfType(typeof(T));
					if (FindObjectsOfType(typeof(T)).Length > 1)
						return m_Instance;
					
					if (m_Instance == null)
					{
						GameObject singleton = new GameObject();
						m_Instance = singleton.AddComponent<T>();
						singleton.name = typeof(T).ToString();
						DontDestroyOnLoad(singleton);
					}
				}
			}
			
			return m_Instance;
		}
	}
	
	public void OnDestroy()
	{
		m_ApplicationIsQuitting = true;
	}
}