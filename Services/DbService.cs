using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.DTOs;
using WebApplication1.Exceptions;
using WebApplication1.Models;

namespace WebApplication1.Services;

public interface IDbService
{
    Task<TripsResponseDto> GetTripsAsync(int page = 1, int pageSize = 10);
    Task RemoveClientAsync(int clientId);
    Task AssignClientToTripAsync(int tripId, ClientTripCreateDto clientData);
}

public class DbService(ApbdContext context) : IDbService
{
    public async Task<TripsResponseDto> GetTripsAsync(int page = 1, int pageSize = 10)
    {
        // Zapytanie z sortowaniem po dacie rozpoczęcia (malejąco)
        var tripsQuery = context.Trips
            .OrderByDescending(t => t.DateFrom)
            .Select(t => new TripGetDto
            {
                Name = t.Name,
                Description = t.Description,
                DateFrom = t.DateFrom,
                DateTo = t.DateTo,
                MaxPeople = t.MaxPeople,
                Countries = t.IdCountries.Select(c => new CountryDto
                {
                    Name = c.Name
                }).ToList(),
                Clients = t.ClientTrips.Select(ct => new ClientDto
                {
                    FirstName = ct.IdClientNavigation.FirstName,
                    LastName = ct.IdClientNavigation.LastName
                }).ToList()
            });

        var totalTrips = await tripsQuery.CountAsync();
        var allPages = (int)Math.Ceiling((double)totalTrips / pageSize);

        var trips = await tripsQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new TripsResponseDto
        {
            PageNum = page,
            PageSize = pageSize,
            AllPages = allPages,
            Trips = trips
        };
    }

    public async Task RemoveClientAsync(int clientId)
    {
        // Sprawdzenie czy klient istnieje
        var client = await context.Clients
            .Include(c => c.ClientTrips)
            .FirstOrDefaultAsync(c => c.IdClient == clientId);

        if (client == null)
        {
            throw new NotFoundException($"Klient o id: {clientId} nie istnieje");
        }

        // Sprawdzenie czy klient ma przypisane wycieczki
        if (client.ClientTrips.Any())
        {
            throw new InvalidOperationException("Nie można usunąć klienta który ma zaplanowane wycieczki.");
        }

        context.Clients.Remove(client);
        await context.SaveChangesAsync();
    }

    public async Task AssignClientToTripAsync(int tripId, ClientTripCreateDto clientData)
    {
        // Sprawdzenie czy wycieczka istnieje i czy data rozpoczęcia jest w przyszłości
        var trip = await context.Trips.FirstOrDefaultAsync(t => t.IdTrip == tripId);
        if (trip == null)
        {
            throw new NotFoundException($"Wycieczka o id: {tripId} nie istnieje");
        }

        if (trip.DateFrom <= DateTime.Now)
        {
            throw new InvalidOperationException("Wycieczka nie aktualna");
        }

        // Sprawdzenie czy klient o podanym PESELu już istnieje
        var existingClient = await context.Clients
            .FirstOrDefaultAsync(c => c.Pesel == clientData.Pesel);

        if (existingClient != null)
        {
            // Sprawdzenie czy klient jest już zapisany na tę wycieczkę
            var existingAssignment = await context.ClientTrips
                .FirstOrDefaultAsync(ct => ct.IdClient == existingClient.IdClient && ct.IdTrip == tripId);

            if (existingAssignment != null)
            {
                throw new InvalidOperationException("Klient jest już zarejestrowany na tą wycieczkę");
            }

            // Dodanie istniejącego klienta do wycieczki
            var clientTrip = new ClientTrip
            {
                IdClient = existingClient.IdClient,
                IdTrip = tripId,
                RegisteredAt = DateTime.Now,
                PaymentDate = clientData.PaymentDate
            };

            await context.ClientTrips.AddAsync(clientTrip);
        }
        else
        {
            // Utworzenie nowego klienta i przypisanie do wycieczki
            var newClient = new Client
            {
                FirstName = clientData.FirstName,
                LastName = clientData.LastName,
                Email = clientData.Email,
                Telephone = clientData.Telephone,
                Pesel = clientData.Pesel
            };

            await context.Clients.AddAsync(newClient);
            await context.SaveChangesAsync(); 

            var clientTrip = new ClientTrip
            {
                IdClient = newClient.IdClient,
                IdTrip = tripId,
                RegisteredAt = DateTime.Now,
                PaymentDate = clientData.PaymentDate
            };

            await context.ClientTrips.AddAsync(clientTrip);
        }

        await context.SaveChangesAsync();
    }
}