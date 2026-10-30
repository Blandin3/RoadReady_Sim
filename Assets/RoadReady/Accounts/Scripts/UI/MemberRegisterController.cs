using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoadReady.Accounts
{
    /// <summary>
    /// Register new member: type a full name, get the next code (R-001, R-002...). If someone with the same name already
    /// registered today it asks "Is that you?" before creating a second person.
    /// </summary>
    public class MemberRegisterController : MonoBehaviour
    {
        RectTransform m_Content, m_Form, m_Confirm;
        TMP_InputField m_Name;
        TMP_Text m_Message, m_Question;
        Button m_RegisterButton;
        MemberRecord m_Existing;

        void Start()
        {
            m_Content = UiKit.CreateScreen("Register new member");
            m_Message = UiKit.Label(m_Content, "", 28f, UiKit.Muted, FontStyles.Normal, 60f);

            m_Form = UiKit.Group(m_Content);
            m_Name = UiKit.MakeInput(m_Form, "Full name");
            m_RegisterButton = UiKit.MakeButton(m_Form, "Register", () => Register(false), height: 72f);

            m_Confirm = UiKit.Group(m_Content);
            m_Question = UiKit.Label(m_Confirm, "", 30f, UiKit.Yellow, FontStyles.Normal, 90f);
            UiKit.MakeButton(m_Confirm, "Yes, that is me", () => Show("Welcome back, " + m_Existing.Label + "."), height: 72f);
            UiKit.MakeButton(m_Confirm, "No, register a new person", () => Register(true), primary: false, height: 72f);

            UiKit.MakeButton(m_Content, "Back to Home", () => SceneRouter.Go(SceneRouter.Home), primary: false, height: 64f);
            UiKit.Keyboard(m_Content);
            UiKit.Focus(m_Name);
            m_Confirm.gameObject.SetActive(false);
        }

        void Show(string message, bool isError = false)
        {
            m_Confirm.gameObject.SetActive(false);
            m_Form.gameObject.SetActive(true);
            m_Name.text = "";
            UiKit.Focus(m_Name);
            UiKit.ShowMessage(m_Message, message, isError);
        }

        async void Register(bool confirmNew)
        {
            m_RegisterButton.interactable = false;
            UiKit.ShowMessage(m_Message, "Registering...", isError: false);
            try
            {
                await AuthService.EnsureReadyAsync();
                if (!AuthService.IsLoggedIn)
                {
                    SceneRouter.Go(SceneRouter.SchoolSetup);
                    return;
                }

                var name = confirmNew && m_Existing != null ? m_Existing.FullName : m_Name.text;
                var result = await MemberService.RegisterAsync(AuthService.SchoolId, name, confirmNew);
                if (result.IsExisting)
                {
                    m_Existing = result.Member;
                    m_Question.text = $"{result.Member.Label} already registered today. Is that you?";
                    m_Form.gameObject.SetActive(false);
                    m_Confirm.gameObject.SetActive(true);
                    UiKit.ShowMessage(m_Message, "", isError: false);
                }
                else
                {
                    Show($"Registered {result.Member.FullName}. Their code is {result.Member.Code}.");
                }
            }
            catch (Exception e)
            {
                UiKit.ShowMessage(m_Message, e.Message);
            }

            m_RegisterButton.interactable = true;
        }
    }
}
