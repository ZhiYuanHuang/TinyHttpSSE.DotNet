using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using TinyHttpSSE.Server;

namespace CompetitionLive.Pages
{
    public class ManageModel : PageModel
    {
        private readonly HttpSseServer _httpSseServer;
        public ManageModel( HttpSseServer httpSseServer) {
            _httpSseServer = httpSseServer;
        }
        public int Score1 { get; set; } = 0;
        public int Score2 { get; set; } = 0;
        public string LastAction { get; set; }
        public void OnGet()
        {
           
        }

        public void OnPost(int score1,int score2,string lastAction) {
            Dictionary<string, object> dict = new Dictionary<string, object>();
            dict["score1"] = score1;
            dict["score2"] = score2;
            dict["lastaction"] = lastAction+"<br />";
            
            _httpSseServer.StreamManagement.All.PushSseMsg(Newtonsoft.Json.JsonConvert.SerializeObject(dict));
        }
    }

}
