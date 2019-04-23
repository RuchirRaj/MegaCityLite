//  Add Platforms here that exclude Quit Menu option
#if !UNITY_EDITOR && (UNITY_PS4)
    #define PLATFORM_EXCLUDES_QUIT_MENU
#endif
using System.Collections;
using System.Collections.Generic;
using Traffic.Simulation;
using UnityEngine;
using UnityEngine.UI;
using Unity.Audio.Megacity;
using Unity.Entities;

public class MainMenu : MonoBehaviour
{
    public class MenuItem
    {
        public Text m_UIText = null;
        public string m_Text = string.Empty;
        public RectTransform m_Rect = null;
        public delegate void OnSelectRoutine();
        public event OnSelectRoutine OnSelectRoutineEvent;
        public delegate void OnSelect();
        public event OnSelect OnSelectEvent;

        public MenuItem(string text, Text uiText)
        {
            m_UIText = uiText;

            if (!text.Equals(string.Empty))
            {
                m_Text = text;
                m_UIText.text = text;
            }
            else
                m_Text = m_UIText.text;

            m_Rect = uiText.GetComponent<RectTransform>();
        }

        public void SelectedRoutine()
        {
            OnSelectRoutineEvent();
        }

        public void SelectedEvent()
        {
            OnSelectEvent();
        }
    }

    public Animator m_MainMenuAnimator = null;
    public RectTransform m_SelectBar = null;
    public List<MenuItem> m_MenuItems = new List<MenuItem>();
    public GameObject m_FlyPath = null;
    public GameObject m_PlayerController = null;
    public AudioMaster m_AudioMaster = null;
    public float m_MenuInputTransitionDelay = 0.1f;

    public enum MenuState
    {
        DISABLED,
        TRANSITIONING,
        ENABLED
    };
    public static MenuState m_MenuState = MenuState.ENABLED;

    private Canvas m_MenuCanvas = null;
    private int m_CurrentMenuItem = 0;
    private int m_NumMenuItems = 0;
    private int m_PrevMenuItem = 0;
    private float m_PrevMenuMoveTime = 0.0f;


    void Awake()
    {
        Transform menuItems = transform.Find("MenuItems");
        m_MenuCanvas = GetComponent<Canvas>();

        if (!m_MenuCanvas.enabled)
            m_MenuCanvas.enabled = true;

        for (int i = 0; i < menuItems.childCount; ++i)
        {
            Transform menuItemTrans = menuItems.GetChild(i);
            Text menuItemText = menuItemTrans.GetComponent<Text>();
            MenuItem menuItem = new MenuItem(menuItemText.text, menuItemText);

            // AJ: this is horrible - sorry
            if (menuItem.m_Text == "On-Rails Flyover")
            {
                menuItem.OnSelectRoutineEvent += OnRailsFlyoverRoutine;
                menuItem.OnSelectEvent += OnRailsFlyover;
            }
            if (menuItem.m_Text == "Player Controller")
            {
                menuItem.OnSelectRoutineEvent += PlayerControllerRoutine;
                menuItem.OnSelectEvent += PlayerController;
            }
            if (menuItem.m_Text == "Quit")
                menuItem.OnSelectRoutineEvent += QuitDemo;

#if PLATFORM_EXCLUDES_QUIT_MENU
            if (menuItem.m_Text == "Quit")
                menuItemText.enabled = false;
            else
#endif
            {
                m_MenuItems.Add(menuItem);
                m_NumMenuItems++;
            }
        }
    }

    public void OnRailsFlyoverRoutine()
    {
        StartCoroutine(AnimateOut(m_MenuItems[m_CurrentMenuItem]));
    }

    public void OnRailsFlyover()
    {
        m_FlyPath.SetActive(true);
        m_PlayerController.SetActive(false);

        World.Active.GetOrCreateManager<TrafficSystem>().SetPlayerReference(GameObject.FindWithTag("Player"));

        if (m_AudioMaster != null)
            m_AudioMaster.GameStarted();
    }

    public void PlayerControllerRoutine()
    {
        StartCoroutine(AnimateOut(m_MenuItems[m_CurrentMenuItem]));
    }

    public void PlayerController()
    {
        m_FlyPath.SetActive(false);
        m_PlayerController.SetActive(true);

        if (m_AudioMaster != null)
            m_AudioMaster.GameStarted();
        World.Active.GetOrCreateManager<TrafficSystem>().SetPlayerReference(GameObject.Find("VehicleControl"));
    }

    public void QuitDemo()
    {
        Application.Quit();
    }

    private IEnumerator SelectItem(MenuItem selectedItem)
    {
        m_MainMenuAnimator.Play("Menu_ItemSelect");

        yield return null;

        while (m_MainMenuAnimator.GetCurrentAnimatorStateInfo(0).IsName("Menu_ItemSelect"))
            yield return null;

        selectedItem.SelectedRoutine();
    }

    private IEnumerator AnimateOut(MenuItem selectedItem)
    {
        selectedItem.SelectedEvent();

        m_MainMenuAnimator.Play("Menu_TransitionOut");

        yield return null;

        while (m_MainMenuAnimator.GetCurrentAnimatorStateInfo(0).IsName("Menu_TransitionOut"))
            yield return null;

        m_MenuState = MenuState.DISABLED;
        gameObject.SetActive(false);
    }

    private IEnumerator UpdateMenuItem(MenuItem prevItem, MenuItem newItem)
    {
        float destY = newItem.m_Rect.anchoredPosition.y ;
        float startY = m_SelectBar.anchoredPosition.y;
        float startTime = Time.unscaledTime;
        bool setTextColour = true;

        while (m_SelectBar.anchoredPosition3D.y != destY)
        {
            Vector3 temp = m_SelectBar.anchoredPosition;
            temp.y = Mathf.SmoothStep(startY, destY, (Time.unscaledTime - startTime) / m_MenuInputTransitionDelay);
            m_SelectBar.anchoredPosition = temp;

            if ((Time.unscaledTime - startTime) > (m_MenuInputTransitionDelay * 0.5f) && setTextColour)
            {
                newItem.m_UIText.color = Color.red;
                prevItem.m_UIText.color = Color.white;
                setTextColour = false;
            }

            yield return null;
        }

        m_MenuState = MenuState.ENABLED;
    }

    private void UpdateInput()
    {
#if !PLATFORM_EXCLUDES_QUIT_MENU
        if (Input.GetKey(KeyCode.Escape)) // Toogle Pause/Resume state
            QuitDemo();
#endif

        if (m_MenuState == MenuState.ENABLED)
        {
            float controllerY = Input.GetAxis("Vertical");

            int oldMenuItem = m_CurrentMenuItem;
            bool moveAllowed = (Time.unscaledTime > (m_PrevMenuMoveTime + m_MenuInputTransitionDelay));

            //	Only restrict update interval with respect to moving, not selecting
            if ((Input.GetKey(KeyCode.DownArrow) || controllerY > 0) && moveAllowed)
                ++m_CurrentMenuItem;
            else if ((Input.GetKey(KeyCode.UpArrow) || controllerY < 0) && moveAllowed)
                --m_CurrentMenuItem;

            else if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.RightArrow) || Input.GetButtonDown("Submit"))
            {
                m_MenuState = MenuState.TRANSITIONING;
                StartCoroutine(SelectItem(m_MenuItems[m_CurrentMenuItem]));
            }

            m_CurrentMenuItem = Mathf.Clamp(m_CurrentMenuItem, 0, (m_NumMenuItems > 0) ? (m_NumMenuItems - 1) : 0);

            if( m_CurrentMenuItem != oldMenuItem )
                m_PrevMenuMoveTime = Time.unscaledTime;

            if (m_CurrentMenuItem != m_PrevMenuItem)
            {
                m_MenuState = MenuState.TRANSITIONING;
                SetMenuOption(m_MenuItems[m_PrevMenuItem], m_MenuItems[m_CurrentMenuItem]);
                m_PrevMenuItem = m_CurrentMenuItem;
            }
        }
    }

    public static MenuState GetMenuState()
    {
        return m_MenuState;
    }

    private void SetMenuOption(MenuItem prevItem, MenuItem newItem)
    {
        StartCoroutine(UpdateMenuItem(prevItem, newItem));
    }

    void Update()
    {
        UpdateInput();
    }
}
