using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;
using System;
using System.Text;
using System.Text.Json;

namespace AzureBot.Bot.Controllers;

[Route("api/[controller]")]
[ApiController]
public class InteractionsController : ControllerBase
{
    private readonly byte[] _rawPublicKey = Convert.FromHexString("265c24669b077eb4b2c5778a04f903025626224d50ae7da2d6537d35bd022651");
    private readonly ILogger<InteractionsController> _logger;
    private readonly SignatureAlgorithm _verificationAlgorithm;
    private readonly PublicKey _verificationPublicKey;

    public InteractionsController(ILogger<InteractionsController> logger)
    {
        _logger = logger;
        _verificationAlgorithm = SignatureAlgorithm.Ed25519;
        _verificationPublicKey = PublicKey.Import(_verificationAlgorithm, _rawPublicKey, KeyBlobFormat.RawPublicKey);
    }

    [HttpPost]
    public IActionResult PostAsync([FromBody] JsonDocument body)
    {
        // Authorizing interactions: https://discord.com/developers/docs/interactions/receiving-and-responding#security-and-authorization
        if (!Request.Headers.TryGetValue("X-Signature-Ed25519", out var sigString))
        {
            return Unauthorized();
        }

        if (!Request.Headers.TryGetValue("X-Signature-Timestamp", out var timestamp))
        {
            return Unauthorized();
        }

        var data = Encoding.UTF8.GetBytes(timestamp + body.RootElement.GetRawText());
        var signature = Convert.FromHexString(sigString);
        if (!_verificationAlgorithm.Verify(_verificationPublicKey, data, signature))
        {
            return Unauthorized();
        }

        if (body.RootElement.GetProperty("type").GetInt32() == 1)
        {
            return Ok(new
            {
                type = 1
            });
        }
        else
        {
            var memberUsername = body.RootElement.GetProperty("member").GetProperty("user").GetProperty("username").GetString();
            return Ok(new
            {
                type = 4,
                data = new
                {
                    content = $"Hello, {memberUsername}",
                }
            });
        }
    }
}
