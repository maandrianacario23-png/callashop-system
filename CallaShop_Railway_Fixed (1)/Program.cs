using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MySql.Data.MySqlClient;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// ✅ Railway MySQL connection - reads from environment variable
string connStr = Environment.GetEnvironmentVariable("MYSQL_CONN") 
    ?? "server=localhost;user=root;password=;database=calla_shop;";

app.UseDefaultFiles();
app.UseStaticFiles();

// ====================== BASIC ENDPOINTS ======================
app.MapPost("/delete-feedback/{id}", async (int id) =>
{
    using var conn = new MySqlConnection(connStr);
    await conn.OpenAsync();
    using var cmd = new MySqlCommand("DELETE FROM feedbacks WHERE id=@id", conn);
    cmd.Parameters.AddWithValue("@id", id);
    await cmd.ExecuteNonQueryAsync();
    return Results.Json(new { success = true });
});

app.MapPost("/delete-order/{id}", async (int id) =>
{
    using var conn = new MySqlConnection(connStr);
    await conn.OpenAsync();
    using var cmd = new MySqlCommand("DELETE FROM orders WHERE id=@id", conn);
    cmd.Parameters.AddWithValue("@id", id);
    await cmd.ExecuteNonQueryAsync();
    return Results.Json(new { success = true });
});

app.MapPost("/submit-order", async (HttpContext context) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(body);

    string orderId = "CALLA-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString().Substring(7);
    string firstName = data["firstName"];
    string lastName = data["lastName"];
    string phone = data["phone"];
    string address = data["address"];
    string payment = data["payment"];
    string items = data["items"];
    decimal total = decimal.Parse(data["total"]);

    using var conn = new MySqlConnection(connStr);
    await conn.OpenAsync();

    string sql = @"INSERT INTO orders 
    (order_id, first_name, last_name, phone, address, payment_method, items, total, status)
    VALUES (@oid, @fn, @ln, @ph, @ad, @pm, @it, @tt, @status)";

    using var cmd = new MySqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@oid", orderId);
    cmd.Parameters.AddWithValue("@fn", firstName);
    cmd.Parameters.AddWithValue("@ln", lastName);
    cmd.Parameters.AddWithValue("@ph", phone);
    cmd.Parameters.AddWithValue("@ad", address);
    cmd.Parameters.AddWithValue("@pm", payment);
    cmd.Parameters.AddWithValue("@it", items);
    cmd.Parameters.AddWithValue("@tt", total);
    cmd.Parameters.AddWithValue("@status", "Pending");

    await cmd.ExecuteNonQueryAsync();
    return Results.Json(new { success = true, orderId });
});

app.MapPost("/complete-order/{id}", async (int id) =>
{
    using var conn = new MySqlConnection(connStr);
    await conn.OpenAsync();
    using var cmd = new MySqlCommand("UPDATE orders SET status='Completed' WHERE id=@id", conn);
    cmd.Parameters.AddWithValue("@id", id);
    await cmd.ExecuteNonQueryAsync();
    return Results.Json(new { success = true });
});

app.MapPost("/submit-feedback", async (HttpContext context) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(body);

    using var conn = new MySqlConnection(connStr);
    await conn.OpenAsync();

    string sql = @"INSERT INTO feedbacks (name, email, rating, message, created_at)
                   VALUES (@name, @email, @rating, @message, NOW())";

    using var cmd = new MySqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@name", data["name"]);
    cmd.Parameters.AddWithValue("@email", data["email"]);
    cmd.Parameters.AddWithValue("@rating", data["rating"]);
    cmd.Parameters.AddWithValue("@message", data["message"]);

    await cmd.ExecuteNonQueryAsync();
    return Results.Json(new { success = true });
});

app.MapPost("/login", async (HttpContext context) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
    if (data["username"] == "admin" && data["password"] == "calla2025")
    {
        context.Response.Cookies.Append("calla_auth", "granted");
        return Results.Json(new { success = true });
    }
    return Results.Json(new { success = false });
});

app.MapGet("/get-feedbacks", async () =>
{
    var feedbacks = new List<object>();
    using var conn = new MySqlConnection(connStr);
    await conn.OpenAsync();

    string sql = "SELECT * FROM feedbacks ORDER BY created_at DESC";
    using var cmd = new MySqlCommand(sql, conn);
    using var reader = await cmd.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        feedbacks.Add(new
        {
            id = Convert.ToInt32(reader["id"]),
            name = reader["name"].ToString(),
            email = reader["email"].ToString(),
            rating = Convert.ToInt32(reader["rating"]),
            message = reader["message"].ToString(),
            created_at = Convert.ToDateTime(reader["created_at"]).ToString("MMMM dd, yyyy hh:mm tt")
        });
    }
    return Results.Json(feedbacks);
});

app.MapGet("/admin", async (HttpContext context) =>
{
    var auth = context.Request.Cookies["calla_auth"];
    if (auth != "granted")
    {
        context.Response.Redirect("/login.html");
        return;
    }

    int totalOrders = 0, totalFeedbacks = 0;
    decimal totalSales = 0;
    double avgRating = 0;

    string orderRows = "";
    string feedbackRows = "";

    using (var conn1 = new MySqlConnection(connStr))
    {
        await conn1.OpenAsync();
        using var cmd = new MySqlCommand("SELECT * FROM orders ORDER BY created_at DESC", conn1);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int id = Convert.ToInt32(reader["id"]);
            string status = Convert.ToString(reader["status"]) ?? "Pending";

            string btnHtml = status == "Pending"
                ? $"<button data-action='complete' data-id='{id}' class='btn-complete'>✅ Complete</button>"
                : $"<button data-action='delete-order' data-id='{id}' class='btn-delete'>🗑 Delete</button>";

            totalOrders++;
            totalSales += Convert.ToDecimal(reader["total"]);

            orderRows += "<tr><td>" + reader["order_id"] + "</td><td>" + reader["first_name"] + " " + reader["last_name"] +
                         "</td><td>" + reader["phone"] + "</td><td>" + reader["address"] + "</td><td>" +
                         reader["payment_method"] + "</td><td>" + reader["items"] + "</td><td>₱" +
                         reader["total"] + "</td><td>" + btnHtml + "</td><td>" +
                         (reader["created_at"]?.ToString() ?? "") + "</td></tr>";
        }
    }

    using (var conn2 = new MySqlConnection(connStr))
    {
        await conn2.OpenAsync();
        using var cmd = new MySqlCommand("SELECT * FROM feedbacks ORDER BY created_at DESC", conn2);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int id = Convert.ToInt32(reader["id"]);
            int rating = Convert.ToInt32(reader["rating"]);
            string stars = new string('★', rating);

            totalFeedbacks++;
            avgRating += rating;

            feedbackRows += "<tr><td>" + reader["name"] + "</td><td>" + reader["email"] +
                            "</td><td style='color:#e8607a;font-size:22px;letter-spacing:4px;'>" + stars +
                            "</td><td>" + reader["message"] + "</td><td>Verified</td><td>" +
                            $"<button data-action='delete-feedback' data-id='{id}' class='btn-delete'>🗑️</button>" +
                            "</td><td>" + (reader["created_at"]?.ToString() ?? "") + "</td></tr>";
        }
    }

    if (totalFeedbacks > 0) avgRating /= totalFeedbacks;

    var html = @"
    <!DOCTYPE html>
    <html>
    <head>
        <title>Calla Admin</title>
        <style>
            body {font-family:Arial,sans-serif;background:#1a0a10;color:white;padding:2rem;}
            h1 {color:#e8607a;}
            .cards {display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:15px;margin:25px 0;}
            .card {background:#2a1118;padding:20px;border-radius:12px;text-align:center;}
            table {width:100%;border-collapse:collapse; margin-top:10px;}
            th {background:#e8607a;padding:12px; text-align:left;}
            td {padding:12px;border-bottom:1px solid #333;}
            tr:hover {background:rgba(232,96,122,0.1);}
            .btn-complete {background:#28a745;color:white;padding:10px 16px;border:none;border-radius:8px;cursor:pointer;font-weight:bold;}
            .btn-delete {background:#dc3545;color:white;padding:10px 16px;border:none;border-radius:8px;cursor:pointer;font-weight:bold;}
            button:hover {opacity:0.9; transform:scale(1.05);}
            #confirmModal {
                display: none; position: fixed; top: 0; left: 0; width: 100%; height: 100%;
                background: rgba(0,0,0,0.85); z-index: 1000; align-items:center; justify-content:center;
            }
            .modal-content {
                background:#2a1118; padding:25px 30px; border-radius:12px; width:320px; text-align:center;
                border: 2px solid #e8607a;
            }
            .modal-content h3 { margin:0 0 15px 0; color:#e8607a; }
            .modal-buttons { margin-top:20px; display:flex; gap:12px; justify-content:center; }
            .modal-btn { padding:10px 24px; border:none; border-radius:6px; cursor:pointer; font-weight:bold; }
            .modal-yes { background:#dc3545; color:white; }
            .modal-no { background:#555; color:white; }
            #toast {
                display: none; position: fixed; bottom: 20px; left: 50%; transform: translateX(-50%);
                background: #28a745; color: white; padding: 14px 24px; border-radius: 8px;
                box-shadow: 0 4px 12px rgba(0,0,0,0.3); z-index: 2000; font-weight: bold;
            }
        </style>
    </head>
    <body>
        <h1>🌸 CALLA ADMIN DASHBOARD</h1>
        <div class='cards'>
            <div class='card'><h2>" + totalOrders + @"</h2><p>Total Orders</p></div>
            <div class='card'><h2>" + totalFeedbacks + @"</h2><p>Feedbacks</p></div>
            <div class='card'><h2>₱" + totalSales.ToString("F2") + @"</h2><p>Total Sales</p></div>
            <div class='card'><h2>" + avgRating.ToString("F1") + @"★</h2><p>Average Rating</p></div>
        </div>
        <input type='text' id='search' placeholder='Search orders or feedbacks...' style='width:100%;padding:12px;margin-bottom:20px;border-radius:8px;'>
        <h1>📦 Orders</h1>
        <table><thead><tr><th>Order ID</th><th>Customer</th><th>Phone</th><th>Address</th><th>Payment</th><th>Items</th><th>Total</th><th>Action</th><th>Date</th></tr></thead>
        <tbody>" + (string.IsNullOrEmpty(orderRows) ? "<tr><td colspan='9' class='no-data'>No orders yet.</td></tr>" : orderRows) + @"</tbody></table>
        <h1>💬 Feedbacks</h1>
        <table><thead><tr><th>Name</th><th>Email</th><th>Rating</th><th>Message</th><th>Status</th><th>Action</th><th>Date</th></tr></thead>
        <tbody>" + (string.IsNullOrEmpty(feedbackRows) ? "<tr><td colspan='7' class='no-data'>No feedbacks yet.</td></tr>" : feedbackRows) + @"</tbody></table>
        <div id='confirmModal'>
            <div class='modal-content'>
                <h3 id='confirmTitle'>Are you sure?</h3>
                <div class='modal-buttons'>
                    <button id='confirmYes' class='modal-btn modal-yes'>Yes</button>
                    <button id='confirmNo' class='modal-btn modal-no'>Cancel</button>
                </div>
            </div>
        </div>
        <div id='toast'></div>
<script>
    let currentAction = null;
    let currentId = null;
    const modal = document.getElementById('confirmModal');
    const title = document.getElementById('confirmTitle');
    const btnYes = document.getElementById('confirmYes');
    const btnNo = document.getElementById('confirmNo');
    const toast = document.getElementById('toast');
    function showConfirm(message, action, id) {
        title.textContent = message;
        currentAction = action;
        currentId = id;
        modal.style.display = 'flex';
    }
    function showToast(message) {
        toast.textContent = message;
        toast.style.display = 'block';
        setTimeout(() => { toast.style.display = 'none'; }, 2500);
    }
    btnYes.addEventListener('click', async () => {
        modal.style.display = 'none';
        if (!currentAction || !currentId) return;
        try {
            let endpoint = '';
            if (currentAction === 'complete') endpoint = '/complete-order/';
            else if (currentAction === 'delete-order') endpoint = '/delete-order/';
            else if (currentAction === 'delete-feedback') endpoint = '/delete-feedback/';
            const res = await fetch(endpoint + currentId, { method: 'POST' });
            const data = await res.json();
            if (data.success) {
                showToast(currentAction === 'complete' ? '✅ Order completed!' : '✅ Deleted!');
                setTimeout(() => location.reload(), 800);
            } else { showToast('❌ Operation failed.'); }
        } catch (err) { showToast('❌ Error occurred.'); }
    });
    btnNo.addEventListener('click', () => { modal.style.display = 'none'; });
    document.addEventListener('click', function(e) {
        const btn = e.target.closest('button[data-action]');
        if (!btn) return;
        const action = btn.dataset.action;
        const id = parseInt(btn.dataset.id);
        if (action === 'complete') showConfirm('Mark this order as completed?', 'complete', id);
        else if (action === 'delete-order') showConfirm('Delete this order?', 'delete-order', id);
        else if (action === 'delete-feedback') showConfirm('Delete this feedback?', 'delete-feedback', id);
    });
    document.getElementById('search').addEventListener('keyup', function() {
        var v = this.value.toLowerCase();
        document.querySelectorAll('tbody tr').forEach(r => {
            r.style.display = r.innerText.toLowerCase().includes(v) ? '' : 'none';
        });
    });
    setInterval(() => location.reload(), 5000);
</script>
    </body>
    </html>";

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(html);
});
app.Run();
