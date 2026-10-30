using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoadReady.Accounts
{
    /// <summary>
    /// First scene: "Register school" (once) or "School login". Firebase remembers the login between launches, so
    /// when the teacher is already logged in this scene skips straight to Home.
    /// </summary>
    public class SchoolSetupController : MonoBehaviour
    {
        RectTransform m_Content;
        RectTransform m_RegisterForm, m_LoginForm;
        Button m_RegisterTab, m_LoginTab, m_RegisterButton, m_LoginButton;
        TMP_InputField m_SchoolName, m_RegEmail, m_RegPassword, m_RegConfirm, m_LoginEmail, m_LoginPassword;
        TMP_Text m_Message;

        async void Start()
        {
            m_Content = UiKit.CreateScreen("RoadReady driving school");
            BuildTabs();
            m_Message = UiKit.Label(m_Content, "Starting...", 26f, UiKit.Muted);
            BuildRegisterForm();
            BuildLoginForm();
            ShowForm(registering: false);
            SetBusy(true);

            try
            {
                await AuthService.EnsureReadyAsync();
                if (AuthService.IsLoggedIn && SceneRouter.Go(SceneRouter.Home))
                    return; // already logged in: nothing to ask
                UiKit.ShowMessage(m_Message, "");
            }
            catch (Exception e)
            {
                UiKit.ShowMessage(m_Message, e.Message);
            }

            SetBusy(false);
        }

        void BuildTabs()
        {
            var row = UiKit.Row(m_Content);
            m_LoginTab = UiKit.MakeButton(row, "School login", () => ShowForm(false));
            m_RegisterTab = UiKit.MakeButton(row, "Register school", () => ShowForm(true));
        }

        void BuildRegisterForm()
        {
            m_RegisterForm = UiKit.Group(m_Content);
            m_SchoolName = UiKit.MakeInput(m_RegisterForm, "School name");
            m_RegEmail = UiKit.MakeInput(m_RegisterForm, "Email");
            m_RegEmail.contentType = TMP_InputField.ContentType.EmailAddress;
            m_RegPassword = UiKit.MakeInput(m_RegisterForm, "Password (at least 6 characters)", password: true);
            m_RegConfirm = UiKit.MakeInput(m_RegisterForm, "Confirm password", password: true);
            m_RegisterButton = UiKit.MakeButton(m_RegisterForm, "Register school", OnRegister);
        }

        void BuildLoginForm()
        {
            m_LoginForm = UiKit.Group(m_Content);
            m_LoginEmail = UiKit.MakeInput(m_LoginForm, "Email");
            m_LoginEmail.contentType = TMP_InputField.ContentType.EmailAddress;
            m_LoginPassword = UiKit.MakeInput(m_LoginForm, "Password", password: true);
            m_LoginButton = UiKit.MakeButton(m_LoginForm, "Log in", OnLogin);
        }

        /// <summary>Shows one form and dims the tab of the form that is open.</summary>
        void ShowForm(bool registering)
        {
            m_RegisterForm.gameObject.SetActive(registering);
            m_LoginForm.gameObject.SetActive(!registering);
            m_RegisterTab.GetComponent<Image>().color = registering ? UiKit.Accent : UiKit.Field;
            m_LoginTab.GetComponent<Image>().color = registering ? UiKit.Field : UiKit.Accent;
            UiKit.ShowMessage(m_Message, "");
        }

        void SetBusy(bool busy)
        {
            foreach (var b in new[] { m_RegisterTab, m_LoginTab, m_RegisterButton, m_LoginButton })
                b.interactable = !busy;
        }

        async void OnRegister()
        {
            if (m_RegPassword.text != m_RegConfirm.text)
            {
                UiKit.ShowMessage(m_Message, "The two passwords are not the same.");
                return;
            }

            await Run("Registering the school...", () => AuthService.RegisterSchoolAsync(m_SchoolName.text, m_RegEmail.text, m_RegPassword.text));
        }

        async void OnLogin() => await Run("Logging in...", () => AuthService.LoginAsync(m_LoginEmail.text, m_LoginPassword.text));

        /// <summary>Runs a login or registration, shows its error if it fails, and goes to Home if it works.</summary>
        async System.Threading.Tasks.Task Run(string busyText, Func<System.Threading.Tasks.Task> action)
        {
            SetBusy(true);
            UiKit.ShowMessage(m_Message, busyText, isError: false);
            try
            {
                await action();
                if (!SceneRouter.Go(SceneRouter.Home))
                    UiKit.ShowMessage(m_Message, "Logged in, but the Home scene is not in Build Settings.");
            }
            catch (Exception e)
            {
                UiKit.ShowMessage(m_Message, e.Message);
            }

            SetBusy(false);
        }
    }
}
