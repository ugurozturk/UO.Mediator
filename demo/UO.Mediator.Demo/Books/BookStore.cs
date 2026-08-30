using System.Collections.Concurrent;
using UO.Mediator.ApiExplorer;
using UO.Mediator.Dispatching;

namespace UO.Mediator.Demo.Books;

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

// Requests with the same ControllerName become actions in one generated partial controller.

[MediatorApiExplorer(ControllerName = "Book")]
public sealed partial record GetListBooksRequest : IRequest<IReadOnlyList<Book>>;

[MediatorApiExplorer(ControllerName = "Book")]
public sealed partial record GetBookRequest(Guid Id) : IRequest<Book?>;

// This metadata is copied to the generated CreateBookAsync action.
[MediatorApiExplorer(ControllerName = "Book")]
[DemoActionMetadata(
    "books.create",
    Description = "Copied from CreateBookRequest to CreateBookAsync.")]
public sealed partial record CreateBookRequest(string Title, string Author) : IRequest<Book>;

[MediatorApiExplorer(ControllerName = "Book")]
public sealed partial record UpdateBookCommand(Guid Id, string Title, string Author) : IRequest<Book?>;

[MediatorApiExplorer(ControllerName = "Book")]
public sealed partial record DeleteBookRequest(Guid Id) : IRequest;

[MediatorApiExplorer(
    ControllerName = "Book",
    Route = "/api/app/books/clear",
    HttpMethod = MediatorHttpMethod.Post)]
public sealed partial record ClearBooksCommand : IRequest;
