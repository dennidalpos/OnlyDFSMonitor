using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DfsMonitor.Web.Pages;

[Authorize]
public class ConfigModel : PageModel
{
    public void OnGet() { }
}
