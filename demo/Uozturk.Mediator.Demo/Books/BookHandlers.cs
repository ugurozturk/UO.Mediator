using Uozturk.Mediator.Dispatching;

namespace Uozturk.Mediator.Demo.Books;

public sealed class GetListBooksHandler(BookStore store)
    : IRequestHandler<GetListBooksRequest, IReadOnlyList<Book>>
{
    public Task<IReadOnlyList<Book>> HandleAsync(GetListBooksRequest request)
    {
        return Task.FromResult(store.GetAll());
    }
}

public sealed class GetBookHandler(BookStore store)
    : IRequestHandler<GetBookRequest, Book?>
{
    public Task<Book?> HandleAsync(GetBookRequest request)
    {
        return Task.FromResult(store.Get(request.Id));
    }
}

public sealed class CreateBookHandler(BookStore store)
    : IRequestHandler<CreateBookRequest, Book>
{
    public Task<Book> HandleAsync(CreateBookRequest request)
    {
        return Task.FromResult(store.Add(request.Title, request.Author));
    }
}

public sealed class UpdateBookHandler(BookStore store)
    : IRequestHandler<UpdateBookCommand, Book?>
{
    public Task<Book?> HandleAsync(UpdateBookCommand request)
    {
        return Task.FromResult(store.Update(request.Id, request.Title, request.Author));
    }
}

public sealed class DeleteBookHandler(BookStore store)
    : IRequestHandler<DeleteBookRequest>
{
    public Task HandleAsync(DeleteBookRequest request)
    {
        store.Delete(request.Id);
        return Task.CompletedTask;
    }
}

public sealed class ClearBooksHandler(BookStore store)
    : IRequestHandler<ClearBooksCommand>
{
    public Task HandleAsync(ClearBooksCommand request)
    {
        store.Clear();
        return Task.CompletedTask;
    }
}
