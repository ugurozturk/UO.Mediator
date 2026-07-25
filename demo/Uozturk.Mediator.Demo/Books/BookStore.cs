using System.Collections.Concurrent;
using Uozturk.Mediator.ApiExplorer;
using Uozturk.Mediator.Dispatching;

namespace Uozturk.Mediator.Demo.Books;

public sealed record Book(Guid Id, string Title, string Author);

/// <summary>
/// Simple in-memory store so the demo needs no database.
/// </summary>
public sealed class BookStore
{
    private readonly ConcurrentDictionary<Guid, Book> _books = new();

    public IReadOnlyList<Book> GetAll() =>
        _books.Values.OrderBy(book => book.Title).ToArray();

    public Book? Get(Guid id) => _books.GetValueOrDefault(id);

    public Book Add(string title, string author)
    {
        var book = new Book(Guid.NewGuid(), title, author);
        _books[book.Id] = book;
        return book;
    }

    public Book? Update(Guid id, string title, string author)
    {
        if (!_books.ContainsKey(id))
        {
            return null;
        }

        var book = new Book(id, title, author);
        _books[id] = book;
        return book;
    }

    public bool Delete(Guid id) => _books.TryRemove(id, out _);

    public void Clear() => _books.Clear();
}

// Requests marked with MediatorApiExplorer get a generated controller each.

[MediatorApiExplorer]
public sealed record GetListBooksRequest : IRequest<IReadOnlyList<Book>>;

[MediatorApiExplorer]
public sealed record GetBookRequest(Guid Id) : IRequest<Book?>;

[MediatorApiExplorer]
public sealed record CreateBookRequest(string Title, string Author) : IRequest<Book>;

[MediatorApiExplorer]
public sealed record UpdateBookCommand(Guid Id, string Title, string Author) : IRequest<Book?>;

[MediatorApiExplorer]
public sealed record DeleteBookRequest(Guid Id) : IRequest;

[MediatorApiExplorer(Route = "/api/app/books/clear", HttpMethod = MediatorHttpMethod.Post)]
public sealed record ClearBooksCommand : IRequest;
