using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoadReady.Accounts
{
    /// <summary>Start training: pick who is training from the school's members, then open the simulator.</summary>
    public class StartTrainingController : MonoBehaviour
    {
        const int k_PageSize = 6;

        RectTransform m_Content, m_List;
        TMP_Text m_Message, m_PageLabel;
        Button m_Previous, m_Next;
        List<MemberRecord> m_Members = new List<MemberRecord>();
        int m_Page;

        async void Start()
        {
            m_Content = UiKit.CreateScreen("Start training");
            m_Message = UiKit.Label(m_Content, "Loading members...", 28f, UiKit.Muted, FontStyles.Normal, 50f);
            m_List = UiKit.Group(m_Content);

            var paging = UiKit.Row(m_Content, 64f);
            m_Previous = UiKit.MakeButton(paging, "Previous", () => ShowPage(m_Page - 1), primary: false);
            m_PageLabel = UiKit.Label(paging, "", 26f, UiKit.Muted);
            m_Next = UiKit.MakeButton(paging, "Next", () => ShowPage(m_Page + 1), primary: false);

            UiKit.MakeButton(m_Content, "Back to Home", () => SceneRouter.Go(SceneRouter.Home), primary: false, height: 72f);
            ShowPage(0);

            try
            {
                await AuthService.EnsureReadyAsync();
                if (!AuthService.IsLoggedIn)
                {
                    SceneRouter.Go(SceneRouter.SchoolSetup);
                    return;
                }

                m_Members = await MemberService.ListAsync(AuthService.SchoolId);
                UiKit.ShowMessage(m_Message, m_Members.Count == 0
                    ? "No members yet. Go back and choose Register new member first."
                    : "Who is training?", isError: m_Members.Count == 0);
                ShowPage(0);
            }
            catch (Exception e)
            {
                UiKit.ShowMessage(m_Message, e.Message);
            }
        }

        void ShowPage(int page)
        {
            var pages = Mathf.Max(1, Mathf.CeilToInt(m_Members.Count / (float)k_PageSize));
            m_Page = Mathf.Clamp(page, 0, pages - 1);

            foreach (Transform child in m_List)
                Destroy(child.gameObject);
            for (var i = m_Page * k_PageSize; i < Mathf.Min(m_Members.Count, (m_Page + 1) * k_PageSize); i++)
            {
                var member = m_Members[i];
                UiKit.MakeButton(m_List, member.Label, () => Train(member), height: 72f);
            }

            m_PageLabel.text = $"Page {m_Page + 1} of {pages}";
            m_Previous.interactable = m_Page > 0;
            m_Next.interactable = m_Page < pages - 1;
        }

        void Train(MemberRecord member)
        {
            CurrentMember.Set(AuthService.SchoolId, member);
            RoadReady.Core.SchoolHandoff.Set(member.FullName, member.Code); // the game skips its own name page and returns here at the end
            if (!SceneRouter.Go(SceneRouter.Game))
                UiKit.ShowMessage(m_Message, "The simulator scene (RR_Bootstrap) is not in Build Settings.");
        }
    }
}
