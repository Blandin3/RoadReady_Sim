using System;
using TMPro;
using UnityEngine;

namespace RoadReady.Accounts
{
    /// <summary>
    /// Home: the school's name and the four things the teacher can do. If nobody is logged in (for example the
    /// session was ended) it sends the teacher back to SchoolSetup.
    /// </summary>
    public class HomeController : MonoBehaviour
    {
        RectTransform m_Content;
        TMP_Text m_SchoolName, m_Message;

        async void Start()
        {
            m_Content = UiKit.CreateScreen("Home");
            m_SchoolName = UiKit.Label(m_Content, "", 34f, UiKit.Accent, FontStyles.Bold, 56f);
            m_Message = UiKit.Label(m_Content, "", 26f, UiKit.Error);

            UiKit.MakeButton(m_Content, "Start training", () => Open(SceneRouter.StartTraining), height: 80f);
            UiKit.MakeButton(m_Content, "Register new member", () => Open(SceneRouter.MemberRegister), height: 80f);
            UiKit.MakeButton(m_Content, "Dashboard", () => Open(SceneRouter.Dashboard), height: 80f);
            UiKit.MakeButton(m_Content, "Log out school", LogOut, primary: false, height: 80f);

            try
            {
                await AuthService.EnsureReadyAsync();
                if (!AuthService.IsLoggedIn)
                {
                    SceneRouter.Go(SceneRouter.SchoolSetup);
                    return;
                }

                var school = await AuthService.GetSchoolAsync();
                m_SchoolName.text = school.Name;
            }
            catch (Exception e)
            {
                UiKit.ShowMessage(m_Message, e.Message);
            }
        }

        void Open(string scene)
        {
            if (!SceneRouter.Go(scene))
                UiKit.ShowMessage(m_Message, "That screen is missing from Build Settings. Run RoadReady > Accounts > Build Account Scenes, then Add Account Scenes To Build Settings.");
        }

        void LogOut()
        {
            AuthService.LogOut();
            SceneRouter.Go(SceneRouter.SchoolSetup);
        }
    }
}
