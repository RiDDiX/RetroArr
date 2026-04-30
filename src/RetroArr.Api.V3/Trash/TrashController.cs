using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Games;

namespace RetroArr.Api.V3.Trash
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class TrashController : ControllerBase
    {
        private readonly TrashService _trash;

        public TrashController(TrashService trash)
        {
            _trash = trash;
        }

        [HttpGet]
        public ActionResult GetAll()
        {
            return Ok(_trash.List());
        }

        [HttpPost("{id}/restore")]
        public ActionResult Restore(string id)
        {
            if (_trash.Restore(id)) return Ok(new { message = "Restored." });
            return Conflict(new { message = "Could not restore - payload missing or original path occupied." });
        }

        [HttpDelete("{id}")]
        public ActionResult PurgeOne(string id)
        {
            if (_trash.PurgeOne(id)) return NoContent();
            return NotFound(new { message = "Entry not found." });
        }

        [HttpDelete]
        public ActionResult EmptyTrash()
        {
            var count = _trash.PurgeAll();
            return Ok(new { purged = count });
        }
    }
}
