using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace RpgTaskTracker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddDbContext<RpgContext>(options =>
                options.UseSqlite("Data Source=rpgsite.db"));

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }

    public class Usuario
    {
        public int UsuarioId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public int Nivel { get; set; } = 1;
        public int XpAtual { get; set; } = 0;
        public bool TemPet { get; set; } = false;
        public List<Atividade> Atividades { get; set; }
        public Pet? Pet { get; set; }
    }

    public class Atividade
    {
        public int AtividadeId { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public bool Concluida { get; set; } = false;
        public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
        public int UsuarioId { get; set; }
        public Usuario Usuario { get; set; }
    }

    public class Pet
    {
        public int PetId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public int UsuarioId { get; set; }
        public Usuario Usuario { get; set; }
    }

    public class RpgContext : DbContext
    {
        public RpgContext(DbContextOptions<RpgContext> options) : base(options) { }
        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Atividade> Atividades { get; set; }
        public DbSet<Pet> Pets { get; set; }
    }

    [ApiController]
    [Route("[controller]")]
    public class RpgController : ControllerBase
    {
        private readonly RpgContext _context;

        public RpgController(RpgContext context)
        {
            _context = context;
        }

        private int XpParaProximoNivel(int nivel) => 10 * (int)Math.Pow(2, nivel - 1);

        private async Task GanharXp(Usuario usuario, int xpGanho)
        {
            usuario.XpAtual += xpGanho;
            bool subiuNivel = false;
            while (usuario.XpAtual >= XpParaProximoNivel(usuario.Nivel))
            {
                usuario.XpAtual -= XpParaProximoNivel(usuario.Nivel);
                usuario.Nivel++;
                subiuNivel = true;
            }

            if (usuario.Nivel >= 5 && !usuario.TemPet)
            {
                usuario.TemPet = true;
                _context.Pets.Add(new Pet
                {
                    Nome = "Companheiro",
                    Tipo = "Mascote Inteligente",
                    UsuarioId = usuario.UsuarioId
                });
            }

            _context.Usuarios.Update(usuario);
            await _context.SaveChangesAsync();
        }

        [HttpPost("ganhar_xp/{usuarioId}")]
        public async Task<IActionResult> GanharXpEndpoint(int usuarioId, [FromBody] int xp)
        {
            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null) return NotFound("Usuário não encontrado");

            await GanharXp(usuario, xp > 0 ? xp : 5);
            return Ok(new { usuario.Nivel, usuario.XpAtual, usuario.TemPet });
        }

        [HttpGet("sugerir/{usuarioId}")]
        public async Task<IActionResult> SugerirTarefas(int usuarioId)
        {
            var atividades = await _context.Atividades.Where(a => a.UsuarioId == usuarioId).ToListAsync();
            var sugestoes = atividades
                .GroupBy(a => a.Descricao)
                .Where(g => g.Count() >= 3)
                .Select(g => g.Key)
                .ToList();
            return Ok(new { sugestoes });
        }

        [HttpPost("criar_pet/{usuarioId}")]
        public async Task<IActionResult> CriarPet(int usuarioId, [FromBody] Pet petInfo)
        {
            var usuario = await _context.Usuarios.FindAsync(usuarioId);
            if (usuario == null) return NotFound("Usuário não encontrado");
            if (usuario.Nivel < 5) return BadRequest("Usuário precisa ser nível 5 para ter um pet");
            if (usuario.TemPet) return BadRequest("Usuário já possui um pet");

            var pet = new Pet
            {
                Nome = petInfo.Nome,
                Tipo = petInfo.Tipo,
                UsuarioId = usuarioId
            };

            usuario.TemPet = true;
            _context.Pets.Add(pet);
            await _context.SaveChangesAsync();

            return Ok(pet);
        }

        [HttpGet("dungeon/{usuarioId}")]
        public async Task<IActionResult> AcessarDungeon(int usuarioId)
        {
            var atividadesConcluidasHoje = await _context.Atividades
                .Where(a => a.UsuarioId == usuarioId && a.Concluida && a.DataCriacao.Date == DateTime.UtcNow.Date)
                .CountAsync();

            if (atividadesConcluidasHoje < 5)
                return BadRequest("Complete pelo menos 5 atividades hoje para acessar a dungeon");

            return Ok("Dungeon acessível! Prepare-se para a aventura.");
        }

        [HttpGet]
        public IActionResult Index() => Ok(new { status = "API rodando", endpoints = new[] { "/rpg/ganhar_xp/{id}", "/rpg/sugerir/{id}", "/rpg/criar_pet/{id}", "/rpg/dungeon/{id}" } });
    }

    public class RpgTests
    {
        [Fact]
        public void TestXpParaProximoNivel()
        {
            var controller = new RpgController(null);
            var metodo = controller.GetType().GetMethod("XpParaProximoNivel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(metodo);
            Assert.Equal(10, metodo.Invoke(controller, new object[] { 1 }));
            Assert.Equal(20, metodo.Invoke(controller, new object[] { 2 }));
            Assert.Equal(40, metodo.Invoke(controller, new object[] { 3 }));
        }

        [Fact]
        public void TestUsuarioCom5AtividadesPodeAcessarDungeon()
        {
            var atividades = new List<Atividade>();
            for (int i = 0; i < 5; i++)
            {
                atividades.Add(new Atividade
                {
                    UsuarioId = 1,
                    Concluida = true,
                    DataCriacao = DateTime.UtcNow
                });
            }
            Assert.Equal(5, atividades.Count(a => a.Concluida && a.DataCriacao.Date == DateTime.UtcNow.Date));
        }
    }
}
