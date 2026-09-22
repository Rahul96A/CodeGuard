using Microsoft.AspNetCore.Mvc;

namespace Banking;

[ApiController]
[Route("api/transfers")]
public sealed class TransfersController(ITransferService transfers) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Transfer(TransferRequest request)
    {
        var receipt = await transfers.TransferAsync(request.FromAccount, request.ToBsb, request.ToAccount, request.Amount);
        return Ok(receipt);
    }
}

public sealed record TransferRequest(string FromAccount, string ToBsb, string ToAccount, decimal Amount);
public interface ITransferService { Task<string> TransferAsync(string from, string bsb, string to, decimal amount); }
